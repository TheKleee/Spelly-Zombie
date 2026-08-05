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
    ///   {"entries":[{"key":"InkMax","value":120},{"key":"ParticleLife","value":6}]}
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

        /// Public door for other tuning blocks (BodyState, StickyBonds, HandGrab)
        /// so every knob in the game reads from the SAME sz_tuning.json.
        public static float Overlay(string key, float def) => O(key, def);

        /// FORCE the type initializer to run in a SAFE context. Without this,
        /// the first touch of DrawingConfig could be a MonoBehaviour FIELD
        /// INITIALIZER during scene load (PlayerInk's `Ink = InkMax`), where
        /// Unity forbids persistentDataPath — the overlay silently died for
        /// the whole session. These hooks run on the main thread BEFORE any
        /// scene deserializes, so the overlay always loads.
        public static void Prime() { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void PrimeRuntime() => Prime();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void PrimeEditor() => Prime();
#endif
        static int Oi(string key, int def) => _overlay.TryGetValue(key, out var v) ? Mathf.RoundToInt(v) : def;

        // ---- Pen / stroke capture ----
        public static readonly float DrawRange = O(nameof(DrawRange), 8f);           // max raycast distance of the pen
        public static readonly float NodeSpacing = O(nameof(NodeSpacing), 0.007f);   // min world distance between nodes. Marko Jul 31: "the runes keep not knowing they are connected — shorten the distance of node creation." Sparse nodes leave real gaps between ink that visually touches, so touch tests miss and one rune reads as several.
        public static readonly float SurfaceOffset = O(nameof(SurfaceOffset), 0.008f); // lift ink off the surface to avoid z-fighting
        public static readonly float MaxStrokeJump = O(nameof(MaxStrokeJump), 0.12f); // hit point jumping further than this in one frame ends the stroke — separate marks STAY separate
        public static readonly float MaxStrokeJumpPerMeter = O(nameof(MaxStrokeJumpPerMeter), 0.02f); // tiny distance allowance (fast flicks split; forgiving seals reconnect what should connect)
        public static readonly int MinStrokeNodes = Oi(nameof(MinStrokeNodes), 2);   // Marko Jul 31: "lines aren't even created when I don't move the mouse far enough". 3 nodes = ~3cm of travel before ANY line existed — and an arrowhead's barb or a LIGHT ray is exactly that short. A limb is not an accidental click; 2 is the minimum that can be a line at all.
        public static readonly float InkWidth = O(nameof(InkWidth), 0.007f);         // line renderer width — THIN pen (Marko: more runes in smaller places)
        public static readonly float DrawSmoothingTime = O(nameof(DrawSmoothingTime), 0.025f); // hand-jitter smoothing time constant, seconds (0 = raw input)
        public static readonly float DrawLookSensitivityScale = O(nameof(DrawLookSensitivityScale), 0.35f); // camera sensitivity multiplier while the pen is down
        public static readonly float EraseRadius = O(nameof(EraseRadius), 0.02f);
        // ---- evaporation (Marko Aug 5: "the longer the game lasts the more it
        // lags — old ink standing there unused should evaporate after 1 minute") ----
        public static readonly float InkEvaporateSeconds = O(nameof(InkEvaporateSeconds), 60f);    // loose world ink lives this long
        public static readonly float InkEvaporateFadeSeconds = O(nameof(InkEvaporateFadeSeconds), 6f); // then thins out over this long before vanishing    // eraser WIDER than the thin pen (Marko: pen-width erasing was impossible to aim) — still small enough for precise corrections; swept along the cursor path so thin ≠ skippy

        // ---- Seal closure / integrity ----
        // Closure requires the ink to basically touch. Ends only link at
        // near-contact; a visible gap never seals. The reliable, exact way to
        // close is to let the lines actually cross (CrossingFinder).
        // (CloseThreshold stays in HAND units, not ink widths — the pen got
        // thinner but fingers didn't get steadier.)
        public static readonly float CloseThreshold = O(nameof(CloseThreshold), 0.018f); // cross-stroke endpoint link distance. Marko Jul 31: "the seal activates when lines don't even touch" — 3.5cm is a visible GAP on a small drawing, and his law is that only TOUCHING ink joins. Halved so the ink really does have to meet.
        /// TOUCHING IS TOUCHING, WHEREVER IT HAPPENS (Marko's most-repeated law:
        /// "only when lines are touching is the main rule for everything: seals
        /// or runes" / "everything must touch exactly"). This is the ONE distance
        /// that means "this ink meets that ink", measured point-to-SEGMENT — to
        /// the LINE, never to the nearest sampled node. Because there is no
        /// sampling error left to hide, it can be honest: 2 × InkWidth, which is
        /// the width of the rendered ink plus a hair. Anything wider is a gap you
        /// can SEE, and a visible gap must never close.
        /// Endpoint-to-endpoint chaining keeps its own, looser CloseThreshold —
        /// that one is in HAND units (two pen tips aimed at the same spot), not
        /// ink units, and it is measured node-to-node by construction.
        public static readonly float InkTouchDistance = O(nameof(InkTouchDistance), 0.014f);
        // SelfCloseFraction/Min/Max DELETED — retired Aug 1 (fraction-of-loop
        // closure made SIZE matter, forbidden); SelfCloseThreshold below
        // replaced them. LoadOverlay ignores stale sz_tuning.json keys.
        public static readonly float BreakDistance = O(nameof(BreakDistance), 0.12f); // an ACTIVE seal opens when a gap grows this far past its drawn length
        public static readonly int MinLoopNodes = Oi(nameof(MinLoopNodes), 8);
        public static readonly float MinLoopPerimeter = O(nameof(MinLoopPerimeter), 0.18f); // palm-sized seals are legal (~6cm triangle)
        public static readonly float MinLoopBulge = O(nameof(MinLoopBulge), 0.06f);  // a loop must enclose something — rejects paper-thin slivers
        public static readonly float GlyphCellMax = O(nameof(GlyphCellMax), 0.13f);  // a self-crossing CELL smaller than this is a rune's inner point (star), not a seal — bigger loops always close
        // MaxLoopGapFraction DELETED — retired Aug 1: a relative gap test let
        // SIZE and PEN-LIFT COUNT decide seal-ness (both forbidden by Marko's
        // standing rules); the per-junction CloseThreshold is the whole law.
        public static readonly int MaxLoopStrokes = Oi(nameof(MaxLoopStrokes), 12);  // DFS depth cap when chaining strokes into one seal — BODY loops split per limb (a circle over crossed arms is 6-8 pieces), so 6 silently refused honest body seals

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
        // RECALIBRATED Aug 1 FOR THE TURN-SEQUENCE MATCHER. The three numbers
        // below are a band, and the band moved: the old stem-and-limb score put
        // an honest draw anywhere from 0.42 to 1.00, so the floor sat at 0.42.
        // The turn sequence is a far tighter metric and its scores are bunched
        // right at the top. Measured on Marko's own 24 wall recordings:
        //   - a fresh drawing scored against his OTHER drawings (leave-one-out,
        //     the harshest case — one template per rune): 0.89 to 1.00, and the
        //     best WRONG rune never exceeds 0.84.
        //   - the same drawings under random rotation, scale x0.14 to x7, one
        //     to four pen lifts and 1.2% hand wobble, 11,474 draws: the right
        //     rune scores 0.99 at the 1st percentile, and a wrong rune won
        //     ZERO times.
        // Leaving the floor at 0.42 under that distribution would have admitted
        // 57% of random scribbles.
        public static readonly float MinRuneScore = O(nameof(MinRuneScore), 0.72f);  // below this the shape is unreadable → fizzle. 0.17 under the worst honest read measured, and it rejects ~78% of random scribbles (0.42 admitted 57% of them)
        public static readonly float RuneAmbiguityMargin = O(nameof(RuneAmbiguityMargin), 0.10f); // two DIFFERENT runes within this of each other = coin flip → fizzle, never misfire. The thinnest margin on an honest draw measured 0.12 (0.14 leave-one-out), so 0.10 costs nothing and 0.15 would start eating real casts
        public static readonly float RuneTrustScore = O(nameof(RuneTrustScore), 0.85f); // a top match AT/ABOVE this is TRUSTED outright — the ambiguity guard only referees weak scribbles. Waves through 99.1% of honest draws and, measured, not one misfire (big wall pools raise every runner-up; honest CHILL/COMPRESS draws kept fizzling on thin gaps — Marko's Jul 22 bug)
        // ONE DRAWING = INK THAT TOUCHES. Aug 1: this was 0.05 — SEVEN ink widths,
        // a plainly VISIBLE gap — and the flood that uses it is transitive, so a
        // row of runes each 4cm apart all got swallowed into one "drawing". That
        // is Marko's complaint verbatim: "it sometimes carries runes along with
        // the seal… even though these are not touching."
        // It was 0.05 to paper over a MEASUREMENT bug, not because touching is
        // 5cm: InkTouches compared node to NODE, so a barb landing on the middle
        // of a shaft measured to the nearest sampled node instead of to the line.
        // InkTouches now measures node-to-SEGMENT (see RuneGlyph), so the barb
        // reads as touching at ~0 — and the number can finally tell the truth.
        // These two must only ever move together; drop the distance without the
        // segment math and visually-touching barbs orphan again.
        public static readonly float RuneTouchDistance = O(nameof(RuneTouchDistance), 0.014f); // = InkTouchDistance: strokes whose INK MEETS read as ONE drawing. Same law as seals — his rule is one rule.
        public static readonly float BodyCastThrowSpeed = O(nameof(BodyCastThrowSpeed), 7f); // body/weapon seals THROW their particles outward at this speed (Marko: on-skin births activated instantly — thrown, siblings fly together and combine mid-air)
        // (old zone-field Blob*/LiquidPool* knobs DELETED; overlay ignores stale keys.)
        // ---- soft body jiggle bones (Marko: "bones drive the shape and have their
        // own colliders to keep the distance from each other and the ground") ----
        public static readonly float BlobBoneSpring = O(nameof(BlobBoneSpring), 220f);  // accel per meter off rest — shape stiffness
        public static readonly float BlobBoneStray = O(nameof(BlobBoneStray), 0.9f);    // leash: a bone may stray at most this ×its own reach from rest — stops bones crossing and locking ("they entangle when dropped")
        public static readonly float BlobBoneDamping = O(nameof(BlobBoneDamping), 9f);  // wobble kill — lower = jigglier
        public static readonly float BlobBoneRadius = O(nameof(BlobBoneRadius), 0.08f); // bone collider size in blob units — their distance-keeping
        // ---- gas: a CLOUD that covers ground, not a balloon (Marko Jul 29) ----
        public static readonly float GasRiseSpeed = O(nameof(GasRiseSpeed), 0.15f);     // terminal climb — barely lifts, so it hangs where you made it
        public static readonly float GasSpreadMax = O(nameof(GasSpreadMax), 5f);        // final cloud size, in multiples of its birth size. Was 2.6 — Marko Aug 4: the vapor is "in too small of an area to ever hit anyone... much larger, and it should grow in time". 7 once read as "ridiculous", but that was a fast balloon-pop; at the slow bloom below, 5× is a hazard that CREEPS over a room.
        public static readonly float GasSpreadPerSec = O(nameof(GasSpreadPerSec), 0.45f); // how fast it swells toward that — a slow bloom, not a pop; at this rate the cloud grows for most of its life
        public static readonly float GasLifeSeconds = O(nameof(GasLifeSeconds), 10f);   // was a hard-coded 2.5s — the cloud died before its growth went anywhere, which is WHY it never hit anyone
        public static readonly float HandLearnMinScore = O(nameof(HandLearnMinScore), 0.95f); // only CLEAN casts silently teach your handwriting — sloppy-but-accepted never joins the pool. Moved with the band (Aug 1): honest reads now start at 0.89, so the old 0.8 would have taught the matcher from EVERY accepted cast, sloppy ones included
        public static readonly float HandLearnCooldown = O(nameof(HandLearnCooldown), 30f);  // seconds between silent handwriting samples (steady learning, no per-cast disk churn)
        public static readonly float WritingPerDeclare = O(nameof(WritingPerDeclare), 0.1f); // a grimoire correction fills 1/10 of the writing bar (Marko: "seemingly maxed out after 10 drawing corrections"); corrections are the ONLY thing that moves it, and it never touches power
        public static readonly float GoodRuneScore = O(nameof(GoodRuneScore), 0.85f); // at/above this the match counts as full strength. Also moved with the band (Aug 1): every honest read measured 0.89 or better, so full strength is the normal outcome and the sloppy-is-weaker gradient only bites between here and MinRuneScore
        public static readonly float MinSizePower = O(nameof(MinSizePower), 0.30f);  // a tiny rune in a big seal still does this fraction of its effect

        // GlyphJoinBase/GlyphJoinSizeFactor DELETED — retired with
        // RuneGlyph.Segment's dead parameters; grouping is the RuneTouchDistance
        // union-find everywhere now. LoadOverlay ignores stale sz_tuning.json keys.

        // ---- Spell effects (rune zones) ----
        public static readonly float ZoneRadiusScale = O(nameof(ZoneRadiusScale), 1.6f); // rune zone radius = drawn rune size × this
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
        public static readonly float InkCostPerMeter = O(nameof(InkCostPerMeter), 11f); // Marko Jul 30: "the wand is really slowly becoming shorter — it needs to lose ink much faster" (was 6/m ≈ 16m per tank; now ≈ 9m, and the lobby cauldron refills forever)
        public static readonly float WandResizeSpeed = O(nameof(WandResizeSpeed), 3.5f); // how fast the wand's LENGTH chases its ink level — the shrink you actually see
        /// Ink needed per kilo to lift a thing — and to tear it out of the
        /// ground, which costs exactly the same (Marko: "the moment you can
        /// easily lift it you should be able to unroot it"). ONE definition:
        /// this default used to be repeated at three call sites and they drifted
        /// apart, so lifting got cheaper while unrooting silently didn't.
        public static readonly float LiftInkPerKg = O(nameof(LiftInkPerKg), 0.22f);
        /// THE WORLD-OR-PROP LINE, in meters of collider bounds. Anything
        /// whose physical bounds exceed this in ANY dimension is WORLD — never
        /// liftable, never given a rigidbody, never made convex — no matter
        /// how much ink is on it. (Marko's fall-through, Aug 4: "Grabbing the
        /// liquid ball made me fall through the ground indefinitely... I guess
        /// I lifted the ground collider just a little bit." His aim ray passed
        /// through the blob and hit the FLOOR — which carried every stroke he
        /// had ever drawn on it, so the tear-loose path out-inked the floor's
        /// anchor and gave the GROUND a dynamic body and a convex hull the
        /// size of the map, with him standing inside it.) 2.5m ≈ a wizard and
        /// a half: every authored prop (bench, chair, crate, cauldron) sits
        /// well under it; floors, facades and canvases sit well over.
        public static readonly float LiftMaxDimension = O(nameof(LiftMaxDimension), 2.5f);
        public static readonly float PropMassKg = O(nameof(PropMassKg), 4f); // weight of any prop you haven't authored a Mass on
        /// Ink is BLACK (his ruling) — the wand, the line and the ore all share it.
        public static readonly Color InkColor = new Color(0.06f, 0.06f, 0.08f, 1f);
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

        // (StickyGripDamping/StickyPressForce/SlickGravityBoost DELETED — the
        // retired sticky zone fields; StickyBonds owns the live ladder.)

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
        public static readonly float ParticleLife = O(nameof(ParticleLife), 4.5f);    // seconds a particle lives (flames 2.5×, lvl2+ 1.5×)

        // ---- Pressure & explosion (density confined by rigid walls) ----
        public static readonly float PressureBuildRate = O(nameof(PressureBuildRate), 0.55f); // pressure/sec per unit gas intensity when fully contained
        public static readonly float ExplodeThreshold = O(nameof(ExplodeThreshold), 1f);      // contained pressure that triggers the burst
        public static readonly float ContainRange = O(nameof(ContainRange), 1.7f);            // a rigid surface within this counts as containing a side
        public static readonly float HeatPressureFactor = O(nameof(HeatPressureFactor), 0.6f); // Heat adds to the gas that pressurizes with Density
        public static readonly float ExplodeRadius = O(nameof(ExplodeRadius), 3.8f);
        public static readonly int ExplodeParticles = Oi(nameof(ExplodeParticles), 48);

        // ---- Persistent ink (characters & weapons) ----
        public static readonly float ReArmDistance = O(nameof(ReArmDistance), 0.10f);  // a spent loop must open this far before it can fire again
        public static readonly float ReCloseDistance = O(nameof(ReCloseDistance), 0.05f); // ...and a re-armed BODY/weapon seal re-fires when its junctions come back within THIS — a forgiving close hand-posing can hit (Schmitt trigger: open >10cm, re-close <5cm). The tight 3.5cm CloseThreshold stays for FRESH world ink only; this is why posing your body in third person casts the seal you drew (it can't reproduce a saved pose to the millimetre)
        public static readonly float SealLimbReach = O(nameof(SealLimbReach), 0.12f);  // a SPENT body seal re-casts when another limb crosses INTO its enclosed loop (Marko: "get my hands near it") — this is how far off the loop's surface a limb may be and still count as inside
        public static readonly int MaxEnvironmentStrokes = Oi(nameof(MaxEnvironmentStrokes), 300);// oldest unsealed world ink fades beyond this (perf cap)

        /// ONE STROKE CLOSES ON ITSELF AT EXACTLY THE SAME DISTANCE TWO STROKES
        /// LINK AT. The parameter is kept for the three call sites (and because
        /// the loop's length is still worth having in a stack trace), but it is
        /// deliberately IGNORED.
        ///
        /// This was `Clamp(loopLength × 0.05, 0.02, 0.06)`, and it broke both of
        /// Marko's standing rules at once:
        ///   · PEN-LIFT COUNT decided. A 1.2m circle drawn in ONE stroke closed
        ///     with its ends 6cm apart — eight ink widths, a plainly visible gap,
        ///     and it fired MID-DRAW before the pen even came back. The SAME
        ///     circle drawn in TWO strokes got CloseThreshold, 1.8cm. A factor of
        ///     three to four, decided by nothing but where he lifted the pen.
        ///     ("A seal drawn in 5 strokes must behave exactly like the same seal
        ///     drawn in one sweep.")
        ///   · SIZE decided. Inside the one-stroke path it was 2cm on a small
        ///     loop and 6cm on a big one, so the bigger you drew, the more air
        ///     the game would close for you. ("Size must never matter.")
        /// It is also the most likely surviving source of "the seal activates
        /// when lines don't even touch" (Jul 31), which is what halving
        /// CloseThreshold was meant to end.
        ///
        /// Overshoot still closes anything, at any size, through CrossingFinder —
        /// which is the exact way, and needs no allowance at all.
        public static float SelfCloseThreshold(float loopLength) => CloseThreshold;
    }
}
