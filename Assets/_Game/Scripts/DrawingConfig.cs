using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// Central tuning constants for the drawing / seal system.
    /// Every value is a default overridable at runtime by
    ///   {Application.persistentDataPath}/sz_tuning.json
    /// Format (only list what you change):
    ///   {"entries":[{"key":"InkMax","value":120},{"key":"ParticleLife","value":6}]}
    /// Keys are the field names. Fields are `static readonly` (not `const`) so
    /// the overlay can reach them and Harmony can patch them.
    public static class DrawingConfig
    {
        // overlay loads first: declared before every tunable below
        [Serializable] class TuneEntry { public string key; public float value; }
        [Serializable] class TuneFile { public TuneEntry[] entries; }
        public const string OverlayFileName = "sz_tuning.json"; // a filename, never tuned → stays const

        // NEVER read persistentDataPath from a field initializer. Unity forbids
        // it during MonoBehaviour construction, and a Element deserialising
        // with the scene is what touches this type FIRST - so the old eager
        // load threw, was swallowed, and every override in sz_tuning.json was
        // silently ignored for the whole session. Loading lazily lets the first
        // safe caller succeed, and an unsafe one simply tries again later.
        static Dictionary<string, float> _overlay;
        static bool _overlayLoaded;

        static Dictionary<string, float> Overlay_()
        {
            if (_overlayLoaded) return _overlay;

            var d = new Dictionary<string, float>();
            try
            {
                string path = Path.Combine(Application.persistentDataPath, OverlayFileName);
                _overlayLoaded = true;   // the path was readable; this answer stands
                if (File.Exists(path))
                {
                    var f = JsonUtility.FromJson<TuneFile>(File.ReadAllText(path));
                    if (f != null && f.entries != null)
                        foreach (var e in f.entries)
                            if (!string.IsNullOrEmpty(e.key)) d[e.key] = e.value;
                    Debug.Log($"[SpellyZombie] tuning overlay: {d.Count} override(s) from {path}");
                }
            }
            catch (Exception)
            {
                // called too early (a field initializer) - leave it unloaded and
                // let the next caller, in a legal context, do the reading
                return _overlay = d;
            }
            return _overlay = d;
        }

        static float O(string key, float def) =>
            Overlay_().TryGetValue(key, out var v) ? v : def;

        /// Overlay accessor for other tuning blocks so every knob reads the same sz_tuning.json.
        public static float Overlay(string key, float def) => O(key, def);

        /// Forces the type initializer to run before any scene deserializes.
        /// A MonoBehaviour field initializer during scene load cannot touch
        /// persistentDataPath, which would silently skip the overlay.
        public static void Prime()
        {
            bool wasBaked = _overlayLoaded;
            Overlay_();
            if (!wasBaked && _overlayLoaded && _overlay.Count > 0 && _tunablesBaked)
                Debug.LogWarning("[SpellyZombie] sz_tuning.json was read AFTER the values were "
                    + "already fixed, so its overrides are not in effect this session. "
                    + "Reload the domain (or restart play) to pick them up.");
        }

        /// Set by the last tunable in the file - if this is true before the
        /// overlay loads, every value took its default.
        static bool _tunablesBaked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void PrimeRuntime() => Prime();

#if UNITY_EDITOR
        // InitializeOnLoadMethod fires DURING a domain reload, while the open
        // scene's objects are being reconstructed - which is exactly the
        // context Unity forbids persistentDataPath in. delayCall waits for the
        // reload to settle, so the read is legal and the overlay actually loads.
        [UnityEditor.InitializeOnLoadMethod]
        static void PrimeEditor() => UnityEditor.EditorApplication.delayCall += () => Prime();
#endif
        static int Oi(string key, int def) =>
            Overlay_().TryGetValue(key, out var v) ? Mathf.RoundToInt(v) : def;

        // ---- Pen / stroke capture ----
        public static readonly float DrawRange = O(nameof(DrawRange), 8f);           // max raycast distance of the pen
        public static readonly float NodeSpacing = O(nameof(NodeSpacing), 0.007f); // min world distance between nodes; sparse nodes leave gaps that make touch tests miss
        public static readonly float SurfaceOffset = O(nameof(SurfaceOffset), 0.008f); // lift ink off the surface to avoid z-fighting
        public static readonly float MaxStrokeJump = O(nameof(MaxStrokeJump), 0.12f); // hit point jumping further than this in one frame ends the stroke
        public static readonly float MaxStrokeJumpPerMeter = O(nameof(MaxStrokeJumpPerMeter), 0.02f); // tiny distance allowance (fast flicks split; forgiving seals reconnect what should connect)
        public static readonly int MinStrokeNodes = Oi(nameof(MinStrokeNodes), 2); // 2 is the minimum that can form a line; arrowhead barbs are that short
        public static readonly float InkWidth = O(nameof(InkWidth), 0.007f); // line renderer width
        public static readonly float DrawSmoothingTime = O(nameof(DrawSmoothingTime), 0.025f); // hand-jitter smoothing time constant, seconds (0 = raw input)
        public static readonly float DrawLookSensitivityScale = O(nameof(DrawLookSensitivityScale), 0.35f); // camera sensitivity multiplier while the pen is down
        public static readonly float EraseRadius = O(nameof(EraseRadius), 0.02f);
        // rune icons inline in text: TMP sprites sit on the baseline
        public static readonly float RuneIconLift = O(nameof(RuneIconLift), 0f);     // em above the baseline. 0 = no tag emitted; the sprite asset's own metrics do the aligning
        public static readonly float RuneIconScale = O(nameof(RuneIconScale), 100f); // % of the surrounding text size. 100 = no tag emitted
        public static readonly float InkEvaporateSeconds = O(nameof(InkEvaporateSeconds), 60f);    // loose world ink lives this long
        /// Rubbed-out ink refills the wand at a loss so casting is never free.
        public static readonly float ScoopRefund = O(nameof(ScoopRefund), 0.5f);
        /// How long a world seal lives after casting before its ink is consumed.
        /// Body seals keep SealProduceSeconds.
        public static readonly float SealConsumeSeconds = O(nameof(SealConsumeSeconds), 1.2f);

        // ---- The pot: one ink pool. Every wand refill bills it, nothing refills it.
        public static readonly float PotPrepSeconds = O(nameof(PotPrepSeconds), 30f);        // inert gather phase, then it opens full
        public static readonly float PotCapacityInk = O(nameof(PotCapacityInk), 1600f);      // total reserve, in wand ink units
        public static readonly float PotCloseRadius = O(nameof(PotCloseRadius), 2.6f);       // fast refill, spill, defuse, corrupt touch inside this
        public static readonly float PotRefillRange = O(nameof(PotRefillRange), 45f);        // beyond this the refill sits at the floor rate
        public static readonly float PotRefillNearPerSec = O(nameof(PotRefillNearPerSec), 45f);  // ink/s at the pot: a full tank in ~2s standing over it
        public static readonly float PotRefillFloorPerSec = O(nameof(PotRefillFloorPerSec), 2.5f); // ink/s across the map - never truly dry, never enough to camp on
        public static readonly float PotSpillPerSec = O(nameof(PotSpillPerSec), 8f);         // full wand inside the close radius: the tap keeps running, the pot pays
        public static readonly float PotCorruptDrainPerSec = O(nameof(PotCorruptDrainPerSec), 11f); // green evaporation drain rate
        public static readonly float PotAcolyteFillPerSec = O(nameof(PotAcolyteFillPerSec), 9f);   // the babysitting tax: their corruption FILLS it
        public static readonly float PotAcolyteFillRadius = O(nameof(PotAcolyteFillRadius), 6f);   // must stay smaller than a sensible overwatch distance
        public static readonly float PotCorruptSeconds = O(nameof(PotCorruptSeconds), 3.2f); // seconds of acolyte touch to turn it green
        public static readonly float PotDefuseSeconds = O(nameof(PotDefuseSeconds), 10f);    // seconds of wizard presence to turn it back (colour only, ink lost is lost)

        // ---- The strike: delivery only, elemental chemistry untouched ----
        public static readonly float StrikeLockRange = O(nameof(StrikeLockRange), 12f);   // snapshot at spawn + hover lock-on radius
        public static readonly float StrikeSpeed = O(nameof(StrikeSpeed), 14f);           // the slam speed
        public static readonly int StrikeBurstPieces = (int)O(nameof(StrikeBurstPieces), 4f); // shatter count on impact - the flying debris that teaches grabbing
        /// Scan ink per meter of the scanned object's largest dimension.
        public static readonly float ScanInkPerMeter = O(nameof(ScanInkPerMeter), 25f);
        public static readonly float InkEvaporateFadeSeconds = O(nameof(InkEvaporateFadeSeconds), 6f); // then thins out over this long before vanishing

        // ---- Seal closure / integrity ----
        // Ends only link at near-contact; a visible gap never seals. The exact
        // way to close is to let the lines cross (CrossingFinder).
        public static readonly float CloseThreshold = O(nameof(CloseThreshold), 0.018f); // cross-stroke endpoint link distance, in hand units
        // Magnet close: a pen-up ending within this of a line end grows one
        // visible bridge segment. 0 disables the assist.
        public static readonly float MagnetCloseRange = O(nameof(MagnetCloseRange), 0.015f);
        /// The one distance that means "this ink meets that ink", measured
        /// point-to-segment, never to the nearest sampled node. 2 × InkWidth.
        /// Endpoint chaining keeps the looser CloseThreshold (hand units).
        public static readonly float InkTouchDistance = O(nameof(InkTouchDistance), 0.014f);
        public static readonly float BreakDistance = O(nameof(BreakDistance), 0.12f); // an ACTIVE seal opens when a gap grows this far past its drawn length
        public static readonly int MinLoopNodes = Oi(nameof(MinLoopNodes), 8);
        public static readonly float MinLoopPerimeter = O(nameof(MinLoopPerimeter), 0.18f); // palm-sized seals are legal (~6cm triangle)
        public static readonly float MinLoopBulge = O(nameof(MinLoopBulge), 0.06f);  // a loop must enclose something - rejects paper-thin slivers
        public static readonly float GlyphCellMax = O(nameof(GlyphCellMax), 0.13f);  // a self-crossing CELL smaller than this is a rune's inner point (star), not a seal - bigger loops always close
        public static readonly int MaxLoopStrokes = Oi(nameof(MaxLoopStrokes), 12);  // DFS depth cap when chaining strokes into one seal; body loops split per limb (6-8 pieces)

        // ---- Seal shape -> variant ----
        // Side count selects which variant of the rune's spell fires; it sets
        // no duration and no lifetime. Every seal produces for a flat time.
        public static readonly int CircleEdges = Oi(nameof(CircleEdges), 10);        // a circle reads as 10 sides = the top variant
        public static readonly float SealProduceSeconds = O(nameof(SealProduceSeconds), 10f); // FLAT. Every seal, every shape.
        public static readonly float CircleMaxVariance = O(nameof(CircleMaxVariance), 0.16f);// radius variation below this = circle; hand circles wobble 10-18%
        public static readonly int CircleMinCorners = Oi(nameof(CircleMinCorners), 8); // and it must not be an obvious low-corner polygon
        public static readonly float RdpEpsilonFactor = O(nameof(RdpEpsilonFactor), 0.015f);// RDP epsilon as fraction of the loop's bounding diagonal
        public static readonly float MinCornerAngle = O(nameof(MinCornerAngle), 20f); // degrees of direction change required to count as an edge corner

        // ---- Zombie navigation ----
        // No navmesh on purpose: maps must work with zero prep, and NavMeshAgent
        // fights the Rigidbody that spells push. Local steering + wall-following.
        public static readonly float ZombieLookAhead = O(nameof(ZombieLookAhead), 1.8f);      // metres probed in front. Bigger = smarter = notices walls sooner
        public static readonly float ZombieProbeRadius = O(nameof(ZombieProbeRadius), 0.35f); // half a zombie's width, so it fits where it thinks it fits
        public static readonly float ZombieStuckSeconds = O(nameof(ZombieStuckSeconds), 0.9f);// trying to move but going nowhere for this long = start wall-following
        public static readonly float ZombieWallFollowSeconds = O(nameof(ZombieWallFollowSeconds), 3f); // how long to commit to hugging one side before trying straight again

        // A summoned zombie is a spell and expires; not the seal's production clock.
        public static readonly float SummonedZombieLife = O(nameof(SummonedZombieLife), 75f);

        /// Pupil color of a zombie somebody else is steering.
        public static readonly Color MindControlEyeColor = new Color(0.85f, 0.10f, 0.10f, 1f);

        // ---- zombie size ----
        // How long the green puff off a dead zombie lingers.
        public static readonly float ZombieDeathCloudSeconds = O(nameof(ZombieDeathCloudSeconds), 2.2f);

        public static readonly float ZombieBodyScale = O(nameof(ZombieBodyScale), 0.49f); // multiplies every zombie body. 1 = the old size
        public static readonly float ZombieEyeScale = O(nameof(ZombieEyeScale), 1.3f);    // eye scale multiplier

        // ---- The summon dials ----
        // Independent axes; only the two ends are authored, everything between
        // interpolates. Dial 1: drawn size to body size, absolute and linear.
        // Cap is 10 because keys 1-9 then 0 address every zombie in overwatch.
        public static readonly int AcolyteZombieCap = Oi(nameof(AcolyteZombieCap), 10);

        // Reference points, not limits: the line runs through them and keeps
        // going in both directions, unclamped. Body size follows the rune's own
        // drawn diameter; defaults assume a rune fills roughly half its seal.
        public static readonly float SummonRuneMin = O(nameof(SummonRuneMin), 0.04f);
        public static readonly float SummonRuneMax = O(nameof(SummonRuneMax), 0.9f);
        public static readonly float SummonSealMin = O(nameof(SummonSealMin), 0.08f); // this seal...
        public static readonly float SummonSizeMin = O(nameof(SummonSizeMin), 0.152f); // ...makes this zombie (0.25m scout)
        public static readonly float SummonSealMax = O(nameof(SummonSealMax), 1.8f);   // and this seal...
        public static readonly float SummonSizeMax = O(nameof(SummonSizeMax), 3.3f);   // ...makes this one (5.4m giant)

        // Physics floor, not balance: Unity's solver tunnels colliders under
        // ~1-2cm through the ground. 0.012 is a ~2cm zombie.
        public static readonly float SummonSizeFloor = O(nameof(SummonSizeFloor), 0.012f);
        // Size drives strength at a 1.6 power, so the small end of the ladder
        // collapses: a 0.15 summon works out to 3 strength and dies to the
        // first thing it touches. Small still means weak, above these.
        public static readonly float SummonMinStrength = O(nameof(SummonMinStrength), 30f);
        public static readonly float SummonMinMass = O(nameof(SummonMinMass), 8f);

        // How far players scatter from the scene anchor when a map has no
        // biomes to place them - the lobby. Wide enough that nobody starts
        // inside anybody.
        public static readonly float LobbyScatterRadius = O(nameof(LobbyScatterRadius), 12f);
        // The host divides the ground: how far apart two spawns must be, and
        // how long a client waits for its point before picking one itself.
        // biggest single hit a client may ask the host for - the old zombie
        // channel carried this, so one packet can never nuke anything.
        // How visible a faded body stays, 1 = normal. These are the numbers the
        // spells start from; author a spell with any value you like.
        public static readonly float FadeTransparency = O(nameof(FadeTransparency), 0.5f);
        public static readonly float FadeCloud = O(nameof(FadeCloud), 0.25f);
        // How fast a CAPACITY (Int, Courage, Clones) moves toward what the
        // ground allows. Slow enough that a spell cast on hostile ground still
        // has a life; fast enough that a place you walk into matters.
        public static readonly float CapacityDriftPerSec = O(nameof(CapacityDriftPerSec), 0.25f);
        public static readonly float NetHitCap = O(nameof(NetHitCap), 60f);
        public static readonly float SpawnApartMeters = O(nameof(SpawnApartMeters), 4f);
        public static readonly float SpawnAssignWaitSeconds = O(nameof(SpawnAssignWaitSeconds), 4f);


        // Dial 2: rune size relative to the seal (already 0..1) to gas radius.
        // The death puff uses the same radius; detonation is always 3x.
        public static readonly float SummonGasRadiusMin = O(nameof(SummonGasRadiusMin), 0.32f);
        public static readonly float SummonGasRadiusMax = O(nameof(SummonGasRadiusMax), 2f);
        // Living aura as a fraction of body height, deliberately tight; the
        // big cloud waits for the corpse.
        public static readonly float PoisonAuraBodyMul = O(nameof(PoisonAuraBodyMul), 0.48f);

        public static readonly float SummonGasDetonateMul = O(nameof(SummonGasDetonateMul), 3f);
        // Poison damage is per second, billed by real dt; kept under the
        // 5-damage camera-shake line so the DoT never shakes the view.
        public static readonly float PoisonDamage = O(nameof(PoisonDamage), 9f);

        // Metres to FX scale: the CFXR poison prefab emits 2-3 unit particles,
        // so the damage radius needs converting. Look only, not reach.
        public static readonly float PoisonFxScale = O(nameof(PoisonFxScale), 0.5f);

        // One puff at a time: FxLibrary drops everything past 8 spawns per
        // frame. Lower = thicker fog.
        public static readonly float PoisonPuffEvery = O(nameof(PoisonPuffEvery), 0.28f);

        // Above this many poison zones the cadence stretches in proportion,
        // holding total smoke constant. FxLibrary pools 12 instances per prefab.
        public static readonly float PoisonFxCrowd = O(nameof(PoisonFxCrowd), 6f);
        // and smoke nobody can see is never spawned at all
        public static readonly float PoisonFxDistance = O(nameof(PoisonFxDistance), 30f);

        // Standing in poison leaves a visible cloud on your head that grows
        // with exposure, fades once you leave, and poisons others while worn.
        public static readonly float PoisonClingRadius = O(nameof(PoisonClingRadius), 0.7f);
        public static readonly float PoisonClingGrow = O(nameof(PoisonClingGrow), 0.55f); // metres per second of exposure
        public static readonly float PoisonClingMax = O(nameof(PoisonClingMax), 3.2f);
        public static readonly float PoisonClingSeconds = O(nameof(PoisonClingSeconds), 4f);

        // An acolyte leaving their disguise breathes out the same zombie poison.
        public static readonly float PoisonExitRadius = O(nameof(PoisonExitRadius), 1.1f);
        public static readonly float PoisonExitSeconds = O(nameof(PoisonExitSeconds), 2.5f);
        public static readonly float PoisonExitCooldown = O(nameof(PoisonExitCooldown), 4f);

        // A dying acolyte gasses off like a detonated zombie. Poison only, no shove.
        public static readonly float PoisonDeathRadius = O(nameof(PoisonDeathRadius), 3f);
        public static readonly float PoisonDeathSeconds = O(nameof(PoisonDeathSeconds), 8f);

        // Daily seed pool: this many map variants per UTC day; the host rolls one per lobby.
        public static readonly float DailySeedPool = O(nameof(DailySeedPool), 16f);

        // The unlock tell: how long an earned page floats at the side, and
        // how wide the paper card is (height follows at 1.3x).
        public static readonly float ButtonHoverScale = O(nameof(ButtonHoverScale), 1.06f); // swell under the cursor - the "this is clickable" tell
        public static readonly float ButtonHoverSpeed = O(nameof(ButtonHoverSpeed), 9f);    // how fast it swells and settles back

        public static readonly float RuneToastSeconds = O(nameof(RuneToastSeconds), 3.5f);
        public static readonly float RuneToastWidth = O(nameof(RuneToastWidth), 260f);  // card WIDTH; height follows the art's own aspect
        public static readonly float RuneToastGap = O(nameof(RuneToastGap), 14f);       // clear space between stacked cards
        public static readonly float RuneToastPopScale = O(nameof(RuneToastPopScale), 0.7f); // pop length as a multiple of the button spring
        public static readonly float RuneToastMargin = O(nameof(RuneToastMargin), 28f); // distance from the left screen edge

        // Detonation: shove and damage scale with the seal's line count.
        // An impulse at or above this knocks you out of draw mode into first
        // person; detonation shoves at 30 center falling to 0 at the rim.
        public static readonly float ShoveBreaksDrawing = O(nameof(ShoveBreaksDrawing), 8f);

        public static readonly float DetonateShove = O(nameof(DetonateShove), 30f);
        public static readonly float DetonateDamage = O(nameof(DetonateDamage), 14f);

        public static readonly float DetonateFieldSeconds = O(nameof(DetonateFieldSeconds), 8f);
        // blast RADIUS floor as a multiple of body height, so the diameter is
        // never under 3x the zombie that blew up, whatever its rune said
        public static readonly float DetonateBodyMul = O(nameof(DetonateBodyMul), 1.5f);
        // how far to look for a zombie under a closing seal...
        public static readonly float DetonateSealReach = O(nameof(DetonateSealReach), 1.1f);
        // ...and how close to its surface the seal must sit to count as drawn on it.
        public static readonly float DetonateSurfaceSlack = O(nameof(DetonateSurfaceSlack), 0.25f);

        // Dial 3: fewer seal lines = stronger. 1.2^(10 - Edges): triangle
        // 3.58x, 9 sides 1.2x, circle (10) exactly 1.0.
        public static readonly float SealLineBonus = O(nameof(SealLineBonus), 1.2f);

        /// How far along the arrow a commanded zombie marches; an arrow is a
        /// heading, not a map pin.
        public static readonly float ZombieMarchDistance = O(nameof(ZombieMarchDistance), 25f);

        /// Total arc a Y fans its zombies across.
        public static readonly float ZombieScatterArc = O(nameof(ZombieScatterArc), 110f);

        /// Summoned dead are green: melee brownish green, ranged light green.
        public static readonly Color SummonMeleeColor = new Color(0.40f, 0.44f, 0.22f, 1f);
        public static readonly Color SummonRangedColor = new Color(0.58f, 0.80f, 0.46f, 1f);

        // ---- Zombie nerve ----
        // Fear is reactive: zombies flee whoever is coming for them and attack
        // players whose back is turned.
        public static readonly float ZombieChaseRange = O(nameof(ZombieChaseRange), 7f);  // a player nearer than this is "on me"; further and I stop panicking
        public static readonly float ZombieBackAngle = O(nameof(ZombieBackAngle), 105f);  // player facing more than this far from me = their back is turned

        // ---- Detection / recognition ----
        public static readonly float DetectInterval = O(nameof(DetectInterval), 0.12f); // how often the seal detector rescans stroke endpoints
        // Recognition law: the right rune fires or none fires. Chamfer knobs
        // (InkChamfer), measured: at .42/.03, 99.4% right-rune, 0.0% wrong.
        // Raise the floor if wrong runes slip through; lower it if honest
        // drawings fizzle too often.
        public static readonly float RuneChamferFloor = O(nameof(RuneChamferFloor), 0.42f); // similarity the best rune must reach to fire at all (chamfer path, currently benched)
        public static readonly float RuneChamferMargin = O(nameof(RuneChamferMargin), 0.03f); // best must beat runner-up by this - a close second = coin flip  fizzle
        // Turn-sequence matcher band, measured on 24 wall recordings: honest
        // draws score 0.89-1.00 leave-one-out (best wrong rune 0.84); under
        // heavy distortion the right rune scores 0.99 at the 1st percentile.
        public static readonly float MinRuneScore = O(nameof(MinRuneScore), 0.72f);  // below this the shape is unreadable and fizzles; rejects ~78% of random scribbles
        public static readonly float RuneAmbiguityMargin = O(nameof(RuneAmbiguityMargin), 0.10f); // two different runes within this = fizzle, never misfire; thinnest honest margin measured 0.12
        public static readonly float RuneTrustScore = O(nameof(RuneTrustScore), 0.85f); // a top match at/above this is trusted outright; the ambiguity guard only referees weak scribbles
        // One drawing = ink that touches, measured node-to-segment (RuneGlyph).
        // This distance and the segment math must move together.
        public static readonly float RuneTouchDistance = O(nameof(RuneTouchDistance), 0.014f); // = InkTouchDistance; same law as seals
        public static readonly float BodyCastThrowSpeed = O(nameof(BodyCastThrowSpeed), 7f); // body/weapon seals THROW their particles outward at this speed
        // The hand throw (E).
        public static readonly float ThrowSpeed = O(nameof(ThrowSpeed), 33f);
        // ---- soft body jiggle bones ----
        public static readonly float BlobBoneSpring = O(nameof(BlobBoneSpring), 220f);  // accel per meter off rest - shape stiffness
        public static readonly float BlobBoneStray = O(nameof(BlobBoneStray), 0.9f);    // leash: a bone may stray at most this × its own reach from rest
        public static readonly float BlobBoneDamping = O(nameof(BlobBoneDamping), 9f);  // wobble kill - lower = jigglier
        public static readonly float BlobBoneRadius = O(nameof(BlobBoneRadius), 0.08f); // bone collider size in blob units - their distance-keeping
        // ---- gas: a CLOUD that covers ground, not a balloon ----
        public static readonly float GasRiseSpeed = O(nameof(GasRiseSpeed), 0.15f);     // terminal climb - barely lifts, so it hangs where you made it
        public static readonly float GasSpreadMax = O(nameof(GasSpreadMax), 5f); // final cloud size, in multiples of its birth size
        public static readonly float GasSpreadPerSec = O(nameof(GasSpreadPerSec), 0.45f); // how fast it swells toward that; the cloud grows for most of its life
        public static readonly float GasLifeSeconds = O(nameof(GasLifeSeconds), 10f);   // cloud lifetime
        public static readonly float HandLearnMinScore = O(nameof(HandLearnMinScore), 0.95f); // only clean casts silently teach your handwriting
        public static readonly float HandLearnCooldown = O(nameof(HandLearnCooldown), 30f);  // seconds between silent handwriting samples (steady learning, no per-cast disk churn)
        public static readonly float WritingPerDeclare = O(nameof(WritingPerDeclare), 0.1f); // a grimoire correction fills 1/10 of the writing bar; corrections alone move it
        public static readonly float GoodRuneScore = O(nameof(GoodRuneScore), 0.85f); // at/above this the match counts as full strength; the sloppy-is-weaker gradient bites between here and MinRuneScore
        public static readonly float MinSizePower = O(nameof(MinSizePower), 0.30f);  // a tiny rune in a big seal still does this fraction of its effect

        // ---- Spell effects (rune zones) ----
        public static readonly float ZoneRadiusScale = O(nameof(ZoneRadiusScale), 1.6f); // SIZE dial: particle size seed = drawn rune size × this
        // Effect-radius dial: zone reach = (rune size / seal radius) × this.
        public static readonly float RuneReachScale = O(nameof(RuneReachScale), 1.0f);
        // Dormant spells: ground seals cast frozen hologram previews at this
        // fraction of true size; anything thrown or released turns live after
        // the universal delay. An untouched preview fades out.
        public static readonly float DormantPreviewScale = O(nameof(DormantPreviewScale), 0.4f);
        public static readonly float WakeDelaySeconds = O(nameof(WakeDelaySeconds), 0.2f);
        public static readonly float DormantLifeSeconds = O(nameof(DormantLifeSeconds), 45f);
        // Preview seek priority: enemy > ally-in-need > sleeping kin > hold.
        public static readonly float DormantSeekRange = O(nameof(DormantSeekRange), 7f);
        public static readonly float DormantSeekSpeed = O(nameof(DormantSeekSpeed), 6.5f); // was 3.4 - the pooling crawl read as "too slow"
        public static readonly float AcolyteBodyScale = O(nameof(AcolyteBodyScale), 0.7f); // hiders are smaller than seekers (the Meccha way)
        // How far off its seal's surface a preview hovers, along the seal's
        // normal; hard-clamped.
        public static readonly float DormantHoverRange = O(nameof(DormantHoverRange), 0.7f);
        // A fresh zombie plays its StandUp climb this long; match the clip length.
        public static readonly float ZombieRiseSeconds = O(nameof(ZombieRiseSeconds), 2f);
        // The meteor's terminal dive.
        public static readonly float MeteorFallSpeed = O(nameof(MeteorFallSpeed), 32f);
        // a scan fills a hidden reserve; it bleeds into the wand at this rate
        // once you are back in your own body, and is thrown away when full
        public static readonly float ReserveFlowPerSec = O(nameof(ReserveFlowPerSec), 18f);

        // a standing flame (HeatEmitter): raw heat/s at its centre. Wood ignites
        // at 200°C, so ~2s of direct contact sets a tossed log alight.
        public static readonly float TorchHeatPerSec = O(nameof(TorchHeatPerSec), 110f);
        public static readonly float BurnThreshold = O(nameof(BurnThreshold), 70f);    // above this °C an object takes burn damage
        // freeze mirrors burn around ambient 18°C; particles stay symmetric ±25
        public static readonly float FreezeThreshold = O(nameof(FreezeThreshold), -34f);
        public static readonly float BurnDamagePerSec = O(nameof(BurnDamagePerSec), 18f);
        public static readonly float FreezeDamagePerSec = O(nameof(FreezeDamagePerSec), 10f);
        public static readonly float AmbientDriftPerSec = O(nameof(AmbientDriftPerSec), 6f); // °C/s an object relaxes back toward ambient
        // extra °C/s per degree away from ambient, so fires end
        public static readonly float AmbientDriftFactor = O(nameof(AmbientDriftFactor), 0.06f);
        public static readonly float ForceAccel = O(nameof(ForceAccel), 25f);         // m/s² a full-strength Density rune applies - beats gravity with room to spare
        public static readonly float DirectionForce = O(nameof(DirectionForce), 40f); // m/s² a Direction rune applies along its arrow - the main mover, and the flight engine
        public static readonly float ArrowZoneDrift = O(nameof(ArrowZoneDrift), 1.1f); // m/s a drawing's zones travel along an enclosed arrow or Y
        // THE TWO VECTORS (Spells V2). Arrow = ATTRACT: drags the target along
        // where the glyph POINTED. Y = REPEL: reverses what the target was
        // doing AND pushes against the Y's heading - the force goes negative.
        public static readonly float VectorPull = O(nameof(VectorPull), 15f);      // the drag/shove along the drawn heading
        public static readonly float VectorReverse = O(nameof(VectorReverse), 1.8f); // how hard Y flips existing momentum
        public static readonly float MusicDangerRange = O(nameof(MusicDangerRange), 14f); // meters: enemy inside this = action music
        // (matter spawning is one block per State zone per activation - size and
        // behaviour live in SurfaceMaterialDB / Matter, not here)
        public static readonly float MaxThermalObjectSize = O(nameof(MaxThermalObjectSize), 3f); // don't cook colliders bigger than this unless they're dynamic (walls/ground)

        // ---- Physics damage (velocity kills: falls and slams) ----
        public static readonly float SafeFallSpeed = O(nameof(SafeFallSpeed), 10f);   // ≈ a 3.5m drop at gravity 14 - free
        public static readonly float FallDamagePerSpeed = O(nameof(FallDamagePerSpeed), 5f); // hp per m/s past safe (10m fall ≈ 33 dmg)
        public static readonly float ImpactDamageSpeed = O(nameof(ImpactDamageSpeed), 8f); // collisions slower than this are harmless (walking, brushing)
        public static readonly float ImpactDamagePerSpeed = O(nameof(ImpactDamagePerSpeed), 4f); // hp per m/s past that

        // ---- air tumble: no jump lasts 2s, so this never interrupts normal
        // jumping; it fires only when you got launched ----
        public static readonly float AirTumbleSeconds = O(nameof(AirTumbleSeconds), 2f);
        public static readonly float AirTumbleRecover = O(nameof(AirTumbleRecover), 0.45f); // landing flop recovery

        // ---- Round game (ink economy / survival loop) ----
        public static readonly float InkMax = O(nameof(InkMax), 100f);
        public static readonly float InkCostPerMeter = O(nameof(InkCostPerMeter), 11f); // ≈9m of line per tank
        public static readonly float WandResizeSpeed = O(nameof(WandResizeSpeed), 3.5f); // how fast the wand's LENGTH chases its ink level - the shrink you actually see
        /// Ink per kilo to lift a thing, and to tear it out of the ground -
        /// one definition on purpose.
        public static readonly float LiftInkPerKg = O(nameof(LiftInkPerKg), 0.22f);
        /// The world-or-prop line, in meters of collider bounds. Bounds over
        /// this in any dimension = world: never liftable, no rigidbody, never
        /// convex. Props sit well under 2.5m; floors and facades well over.
        public static readonly float LiftMaxDimension = O(nameof(LiftMaxDimension), 2.5f);
        public static readonly float PropMassKg = O(nameof(PropMassKg), 4f); // weight of any prop you haven't authored a Mass on
        /// Ink is black; wand, line and ore share it.
        public static readonly Color InkColor = new Color(0.06f, 0.06f, 0.08f, 1f);
        /// Corrupt ink is green. An acolyte's wand is green outright; a
        /// corrupting wizard's greens from the tip down.
        public static readonly Color CorruptInkColor = new Color(0.25f, 0.62f, 0.16f, 1f);
        public static readonly float InkPerKill = O(nameof(InkPerKill), 10f);        // shared to ALL players per zombie down - the fast lane
        // ---- The wand economy ----
        // Wizard: no passive regen; the pot is the only well (CauldronEconomy.LocalWandTick).
        // Acolyte: no pot; ink evaporates and returns only from scanning.
        public static readonly float AcolyteInkEvaporatePerSec = O(nameof(AcolyteInkEvaporatePerSec), 1.6f);

        // Fraction of InkMax granted at spawn; 0 restores a wandless start.
        public static readonly float StartInkFraction = O(nameof(StartInkFraction), 1f);

        // ---- The wand tip flow ----
        // Mote direction = gaining or losing ink; mote size = how fast.
        // Rates are in ink fraction per second: pot up close ~0.45, drawing
        // ~0.2, far floor ~0.025, acolyte evaporation ~0.016.
        public static readonly float WandFlowDeadzone = O(nameof(WandFlowDeadzone), 0.008f); // below this the tip is quiet
        public static readonly float WandFlowFullRate = O(nameof(WandFlowFullRate), 0.25f);  // at or above this the motes are at full size
        public static readonly float WandMoteMin = O(nameof(WandMoteMin), 0.005f);           // world metres - a trickle
        public static readonly float WandMoteMax = O(nameof(WandMoteMax), 0.026f);           // world metres - standing in the cauldron

        // The pot's own scale: rising motes = evaporating, falling motes = an
        // acolyte refilling it. Pot fill moves far slower than a wand.
        public static readonly float PotFlowDeadzone = O(nameof(PotFlowDeadzone), 0.0006f);
        public static readonly float PotFlowFullRate = O(nameof(PotFlowFullRate), 0.02f);

        // Zombies loitering at a black pot corrupt it too, ~8x slower than an acolyte.
        public static readonly float PotZombieCorruptFactor = O(nameof(PotZombieCorruptFactor), 0.12f);

        // Ink moves only when its pot is shattered: beam up, this many seconds
        // of drought, then splash into a random surviving pot. Broken stays
        // broken; the last break grounds the ink at the InkGrave.
        public static readonly float PotHopGapSeconds = O(nameof(PotHopGapSeconds), 10f);

        // The lobby pot behaves like the real one but rebrews itself full and
        // black on this period.
        public static readonly float LobbyPotRefreshSeconds = O(nameof(LobbyPotRefreshSeconds), 10f);

        // Ink touching a zombie pins it in rest pose this long past the last
        // touch, so the canvas doesn't walk off between strokes.
        public static readonly float ZombiePaintFreezeSeconds = O(nameof(ZombiePaintFreezeSeconds), 2.5f);

        // How long a broken lobby prop stays gone before rebuilding itself.
        public static readonly float LobbyRespawnSeconds = O(nameof(LobbyRespawnSeconds), 15f);

        // The meteor: born at the seal, thrown up, swelling as it climbs, then
        // plain gravity.
        public static readonly float MeteorRiseSpeed = O(nameof(MeteorRiseSpeed), 13f);
        public static readonly float MeteorGrowth = O(nameof(MeteorGrowth), 3.5f);   // final size vs birth size
        public static readonly float MeteorGrowSeconds = O(nameof(MeteorGrowSeconds), 0.9f);

        // Fusions add their parents' drawn size (SpellParticle.FuseSize); this
        // caps a long chain.
        public static readonly float FusedSizeCap = O(nameof(FusedSizeCap), 12f);

        // The smallest size a rune can carry - the floor of z.Radius in
        // Spell.cs - where SpellParticle.SizeMul returns exactly 1. One
        // constant so floor and reference cannot drift apart.
        public static readonly float RuneSizeMin = O(nameof(RuneSizeMin), 0.9f);
        // and the ceiling on how far a fused spell may outgrow that neutral
        public static readonly float FusedSizeMulMax = O(nameof(FusedSizeMulMax), 3f);

        // Drain while the pen is down, on top of the per-metre cost.
        public static readonly float WandDrainPerSec = O(nameof(WandDrainPerSec), 14f);
        public static readonly float CauldronInkPerSec = O(nameof(CauldronInkPerSec), 22f); // standing at any cauldron refills fast
        public static readonly float HolyLightPerSec = O(nameof(HolyLightPerSec), 11f); // light zones damage undead
        public static readonly float MidDrawCloseStartRegion = O(nameof(MidDrawCloseStartRegion), 0.12f); // mid-draw closure only onto the stroke's first 12cm (the circle gesture) - deeper self-crossings are glyphs, not lassos
        public static readonly float SolidDropHeight = O(nameof(SolidDropHeight), 3f); // solid conjures materialize OVERHEAD and drop - gravity is the damage
        public static readonly float InkTricklePerSec = O(nameof(InkTricklePerSec), 4f); // extra intermission refill
        public static readonly float IntermissionSeconds = O(nameof(IntermissionSeconds), 20f);
        public static readonly int MaxRounds = Oi(nameof(MaxRounds), 10);            // demo round cap
        // read through Sides.MaxHealthFor
        public static readonly float WizardMaxHealth = O(nameof(WizardMaxHealth), 140f);
        public static readonly float AcolyteMaxHealth = O(nameof(AcolyteMaxHealth), 90f);
        // Out-of-danger mending, never above your own max. THE RATE IS DERIVED
        // FROM THAT MAX, never per side: the lower your ceiling the faster you
        // recover, so acolytes (90) mend faster than wizards (140) with no
        // side rule, and BUFFING - which raises the ceiling - costs recovery
        // speed. rate = RegenAtRefMax * (RegenRefMax / max)^RegenFalloff.
        // strength IS health: lifting and throwing scale with how much of your
        // ceiling is left. This is the floor at 0 strength - never 0, a dying
        // wizard still shoves, feebly.
        public static readonly float StrengthFloorMul = O(nameof(StrengthFloorMul), 0.35f);
        // how fast strength SETTLES down to a lower ceiling (walking into a
        // weak biome). Slow enough that leaving in time saves you.
        public static readonly float StrengthSettlePerSec = O(nameof(StrengthSettlePerSec), 6f);
        // the environment obeys the same law: a prop too weak for its own mass
        // buckles. Load = (mass x PropWeightPerKg) / its strength.
        // Spread thins a body until it cannot hold together and SPLITS - which
        // is why Spread multiplies things without any duplication rule.
        // Density = mass / scale^3, so a big light thing divides first.
        public static readonly float SplitMinDensity = O(nameof(SplitMinDensity), 0.6f);
        public static readonly float SplitPush = O(nameof(SplitPush), 1.6f); // how far the halves shove apart
        // BUOYANCY, not flight: you rise when the medium is denser than you.
        // The medium is the BIOME - normally gas, sometimes liquid - and its
        // DensityOffset is that density, so thin peak air and heavy deep air
        // are authored as biome boxes, never as altitude maths.
        public static readonly float FloatRiseMax = O(nameof(FloatRiseMax), -0.35f); // hardest upward drift
        // what each phase of medium weighs. Liquid sits just under a normal
        // body, so you bob and swim without a swim MODE; solid is future
        // digging, where strength will matter as much as density.
        public static readonly float LiquidMediumDensity = O(nameof(LiquidMediumDensity), 0.92f);
        public static readonly float SolidMediumDensity = O(nameof(SolidMediumDensity), 2.5f);
        public static readonly float SwimAt = O(nameof(SwimAt), 0.55f); // medium control that counts as swimmable
        public static readonly float FloatDrag = O(nameof(FloatDrag), 1.4f);         // drifting, never shooting off
        // how much lighter than the air you must be to SWIM through gas the
        // way you swim through liquid

        // A creature's strength ceiling from its own body: bigger and heavier
        // is stronger. Size dominates (power > 1), mass adds on top - a giant
        // is strong because it is big, and heavier again because it is dense.
        public static readonly float BodyStrengthBase = O(nameof(BodyStrengthBase), 60f);
        public static readonly float BodyStrengthSizePower = O(nameof(BodyStrengthSizePower), 1.6f);
        public static readonly float BodyStrengthPerKg = O(nameof(BodyStrengthPerKg), 0.05f);

        // ---- THE THRESHOLD ENGINE (Spells V2) ----
        // an axis counts toward a fusion once its magnitude passes this
        public static readonly float FusionAt = O(nameof(FusionAt), 0.8f);

        // ★ THE LINES, in human units: how far along an axis before a thing
        // counts as that thing. 20 degrees of heat is warm, not a fire;
        // 15 percent of light is a glow, not a light spell.
        public static readonly float LineTemp = O(nameof(LineTemp), 20f);
        public static readonly float LinePercent = O(nameof(LinePercent), 15f);
        public static readonly float LineStrength = O(nameof(LineStrength), 5f);
        public static readonly float LineClones = O(nameof(LineClones), 1f);

        // ---- AXIS CEILINGS -------------------------------------------------
        // Every axis tops out. Without a ceiling, stacking heat on something
        // burning keeps it burning forever; with one it maxes, drift starts
        // pulling it back, and keeping a thing alight costs repeated casting.
        // In UNITS, so one number is the same size of push on every axis.
        public static readonly float AxisCap = O(nameof(AxisCap), 4f);
        // Int and Courage: 0 is mindless / terrified, and neither goes below it.
        public static readonly float CapacityCap = O(nameof(CapacityCap), 4f);
        // Whole copies of yourself. THREE, his ruling - the keyboard could
        // address ten but ten is not the number.
        public static readonly float CloneCap = O(nameof(CloneCap), 3f);
        // Temperature is the one axis carried in degrees, so it gets degree
        // bounds - the range Thermal always used.
        public static readonly float TempCeiling = O(nameof(TempCeiling), 900f);
        public static readonly float TempFloor = O(nameof(TempFloor), -320f);

        // How far from its own natural temperature a LIVING thing can go before
        // it suffers. These are the old player band (37 natural, 15..45)
        // expressed as distances, so flesh keeps the tolerance it always had
        // while stone gets the wide one it always had.
        public static readonly float LivingHeatTolerance = O(nameof(LivingHeatTolerance), 8f);
        public static readonly float LivingChillTolerance = O(nameof(LivingChillTolerance), -22f);
        // damage per degree outside the band, per second
        public static readonly float TempDamagePerDegree = O(nameof(TempDamagePerDegree), 0.9f);
        // a lvl3 spell is a temporary biome: how long the rewritten nature lasts
        public static readonly float SpellBiomeSeconds = O(nameof(SpellBiomeSeconds), 10f);
        // a spell bursting on terrain leaves its data hanging in the air this long
        public static readonly float LingerSeconds = O(nameof(LingerSeconds), 4f);
        // the drawn SrcSize at which a particle wears exactly its base body size
        public static readonly float ParticleSizeNeutral = O(nameof(ParticleSizeNeutral), 0.4f);
        // HIS COUPLING TABLE: the effect axes are byproducts of the data axes
        public static readonly float CoupleLumCourage = O(nameof(CoupleLumCourage), 0.5f);
        public static readonly float CouplePressureStrength = O(nameof(CouplePressureStrength), 0.25f);
        public static readonly float CouplePressureClones = O(nameof(CouplePressureClones), 0.5f);
        public static readonly float CoupleStateMind = O(nameof(CoupleStateMind), 0.5f);
        public static readonly float CoupleAffinityCourage = O(nameof(CoupleAffinityCourage), 0.4f);
        public static readonly float CoupleAffinityMind = O(nameof(CoupleAffinityMind), 0.4f);
        public static readonly float CoupleBalanceMind = O(nameof(CoupleBalanceMind), 0.4f);
        public static readonly float CoupleBalanceStrength = O(nameof(CoupleBalanceStrength), 0.2f);
        // range of influence: how far an object's data reaches a neighbour, and the exchange rate
        public static readonly float InfluenceReach = O(nameof(InfluenceReach), 1.2f);
        public static readonly float InfluenceSharePerSec = O(nameof(InfluenceSharePerSec), 0.15f);
        // the one gravity: affinity force at full axis strength
        public static readonly float AffinityForce = O(nameof(AffinityForce), 9f);

        // How often the host tells everyone what things in the world currently
        // are. Slower than the beat: being on fire is not a per-frame fact.
        public static readonly float StateSyncSeconds = O(nameof(StateSyncSeconds), 0.5f);

        // However far an area starts from its spell, it rushes back toward it
        // at this speed. That is what makes a meteor fall.
        public static readonly float AreaHomingSpeed = O(nameof(AreaHomingSpeed), 30f);

        // A golem sheds its area around itself - rocks for solid, blobs for
        // liquid. How often, how hard, and what share of itself each carries.
        public static readonly float GolemShedSeconds = O(nameof(GolemShedSeconds), 1.4f);
        public static readonly float GolemShedSpeed = O(nameof(GolemShedSpeed), 4.5f);
        public static readonly float GolemShedShare = O(nameof(GolemShedShare), 0.3f);

        // What a thing breaks into when it dies still holding something. The
        // pieces share what was left, so each generation carries less and the
        // chain ends on its own.
        public static readonly int ScatterPieces = Oi(nameof(ScatterPieces), 4);
        public static readonly float ScatterSpeed = O(nameof(ScatterSpeed), 7f);

        // ---- the charge (golems and zombies share ChargeAttack) ----
        // The tell is the fairness: you get this long to read the hop and move.
        // The direction locks when the tell starts, so a charge NEVER homes.
        public static readonly float ChargeTellSeconds = O(nameof(ChargeTellSeconds), 0.7f);
        // how wide the eyes go on the tell, as a multiple of the size they
        // were AUTHORED at. 1 = they do not move.
        public static readonly float ChargeTellEyeSwell = O(nameof(ChargeTellEyeSwell), 1.25f);
        public static readonly float ChargeTellHop = O(nameof(ChargeTellHop), 3.2f);
        public static readonly float ChargeRunSeconds = O(nameof(ChargeRunSeconds), 1.3f);
        public static readonly float ChargeSpeed = O(nameof(ChargeSpeed), 11f);
        public static readonly float ChargeShove = O(nameof(ChargeShove), 12f);
        // dazed after the hit: it stands there a beat, then walks, and only
        // after the cooldown lines another one up
        public static readonly float ChargeRecoverSeconds = O(nameof(ChargeRecoverSeconds), 1.1f);
        // matter size -> golem scale. Merges add volume, so two blobs raise a
        // bigger golem than one, and it keeps growing as more join. The floor
        // matters: a golem must always be a creature you can fight, never a
        // paper pebble that dies before it reaches you.
        public static readonly float GolemSizePerMatter = O(nameof(GolemSizePerMatter), 2f);
        public static readonly float GolemMinScale = O(nameof(GolemMinScale), 0.12f);
        public static readonly float GolemMaxScale = O(nameof(GolemMaxScale), 3f);
        public static readonly float GolemBaseMass = O(nameof(GolemBaseMass), 45f); // at scale 1
        // SIZE AND TOUGHNESS ARE SEPARATE for golems. A small one should still
        // be worth fighting, so strength never falls below this however little
        // matter raised it - bigger still means stronger, it just starts here.
        public static readonly float GolemMinStrength = O(nameof(GolemMinStrength), 90f);
        public static readonly float GolemMinMass = O(nameof(GolemMinMass), 12f);
        // below this height it is under the world: kill it there so it dies
        // visibly instead of falling out of sight forever
        public static readonly float GolemFloorY = O(nameof(GolemFloorY), -25f);
        // it cannot be finished off while rising - a golem always gets to take
        // its first step, however hard the ground it was raised on is fought over
        public static readonly float GolemBirthShield = O(nameof(GolemBirthShield), 2f);
        // golems walk by skipping - a hop every so often instead of a glide
        public static readonly float GolemSkipEvery = O(nameof(GolemSkipEvery), 0.55f);
        public static readonly float GolemSkipHop = O(nameof(GolemSkipHop), 2.1f);

        // What a biome stamps onto whatever it raises (BiomeStamp).
        public static readonly float BiomeTintStrength = O(nameof(BiomeTintStrength), 0.35f); // bounded: the melee/ranged read must survive
        public static readonly float ResistFullAt = O(nameof(ResistFullAt), 25f);   // axis amount that earns the full cut
        public static readonly float ResistMaxCut = O(nameof(ResistMaxCut), 0.45f); // HARD CAP - helpful, never immunity
        public static readonly float PropWeightPerKg = O(nameof(PropWeightPerKg), 0.06f);
        public static readonly float PropCrushLoad = O(nameof(PropCrushLoad), 3f);   // below this nothing buckles
        public static readonly float PropCrushPerSec = O(nameof(PropCrushPerSec), 3f);
        public static readonly float RegenAtRefMax = O(nameof(RegenAtRefMax), 1.5f); // rate for a thing whose max IS RegenRefMax
        public static readonly float RegenRefMax = O(nameof(RegenRefMax), 100f);
        public static readonly float RegenFalloff = O(nameof(RegenFalloff), 1f);     // 1 = plain inverse; higher punishes big ceilings harder
        public static readonly float RegenCalmSeconds = O(nameof(RegenCalmSeconds), 5f); // undamaged this long before mending starts
        // ragdolling in open air without gaining height for this long kills you
        public static readonly float FallDeathSeconds = O(nameof(FallDeathSeconds), 10f);
        // public lobbies refuse joiners whose estimated ping to the host exceeds this (0 = no gate)
        public static readonly int LobbyMaxPingMs = Oi(nameof(LobbyMaxPingMs), 150);
        public static readonly float ReviveSeconds = O(nameof(ReviveSeconds), 3f);
        public static readonly float ReviveRange = O(nameof(ReviveRange), 2.5f);

        // ---- Luminance ----
        // plain light does no damage; the plasma ladder earns it (light + density, lightning, laser)
        public static readonly float BlindSeconds = O(nameof(BlindSeconds), 1.5f);   // darkness re-applies this while a creature stays inside

        // ---- GRAMMAR v4 (SPELL_PARTICLES.md - leveling, paradox, lineage) ----
        public static readonly float Lvl2AuraRadius = O(nameof(Lvl2AuraRadius), 1.15f); // a lvl2 particle radiates this far
        // How much wider the strongest coat can push that reach. A flame at
        // twice its threshold covers twice the ground; this stops a maxed-out
        // one covering the map.
        public static readonly float AuraInfluenceMax = O(nameof(AuraInfluenceMax), 3f);

        // How much of itself a particle gives away per beat when radiating,
        // and all at once when it lands on something.
        public static readonly float AuraShare = O(nameof(AuraShare), 0.35f);
        public static readonly float TouchShare = O(nameof(TouchShare), 1f);
        // Balance is the one axis that has to become physics: how long it
        // holds a creature, and how hard it brakes a rigidbody.
        public static readonly float GripSeconds = O(nameof(GripSeconds), 2f);
        public static readonly float GripBrake = O(nameof(GripBrake), 7f);
        // How big the mote itself gets once it is imposing. Its REACH is
        // AuraRadius as always; this is just the visible core.
        public static readonly float BiomeMoteScale = O(nameof(BiomeMoteScale), 0.45f);
        // How long a spell biome holds the ground before handing it back. It
        // cannot be forever: a wizard would rewrite the island permanently.
        // Scaled by how big the drawing was, up to three times this.
        public static readonly float BiomeSeconds = O(nameof(BiomeSeconds), 10f);
        // How long a particle takes to move its bones into a new shape. It is
        // one model being re-posed, so becoming a tornado can be a growth
        // rather than a cut.
        public static readonly float ShapeMorphSeconds = O(nameof(ShapeMorphSeconds), 0.35f);

        // Strength is a capacity: how fast you give up what the ground cannot
        // hold, and how fast you mend back toward what it can.
        public static readonly float StrengthYieldPerSec = O(nameof(StrengthYieldPerSec), 12f);
        public static readonly float RegenBase = O(nameof(RegenBase), 3f);
        // the maximum a RegenBase-speed thing has; anything with a higher
        // ceiling mends proportionally slower
        public static readonly float RegenReference = O(nameof(RegenReference), 100f);

        // Courage at which a thing stops being afraid of anything at all.
        // Below it, fear scales - a coward panics at what a braver one ignores.
        public static readonly float FearlessAt = O(nameof(FearlessAt), 2f);

        // SPREADING: how far a spreading thing reaches for its next victim,
        // and what share of its own numbers it hands over. Under 1 so a fire
        // cools as it travels instead of copying itself forever.
        public static readonly float SpreadReach = O(nameof(SpreadReach), 2.2f);
        public static readonly float SpreadShare = O(nameof(SpreadShare), 0.45f);

        // How much stronger a charger is for the length of its charge. It pays
        // its own impact like everything else; this is what lets it survive
        // the hit it aimed. Below 1 and ramming a wall kills it.
        public static readonly float ChargeStrengthMul = O(nameof(ChargeStrengthMul), 2.5f);
        // a golem lives this long, however it was raised
        public static readonly float GolemLifeSeconds = O(nameof(GolemLifeSeconds), 30f);
        // how far a standing attract/repel mote reaches with its pull
        public static readonly float AffinityReach = O(nameof(AffinityReach), 3.5f);
        // what fraction of the SPELL's own axes an element hands a neighbour
        // per spread beat (SpreadShare is taken: it is PoisonField's radius
        // growth rate, a different thing wearing the same name)
        public static readonly float SpreadTransferShare = O(nameof(SpreadTransferShare), 0.25f);

        // The puddle a goo lands in. Its BITE and whether it spreads come from
        // the Goo row; these two are the shape of the splash itself.
        public static readonly float GooRadius = O(nameof(GooRadius), 1.4f);
        public static readonly float GooSeconds = O(nameof(GooSeconds), 4f);
        // ---- the ranged zombie's own artillery ----
        // honest reach: particle drag caps the real flight near 10m, and a
        // 16m trigger made far shots land at the zombie's own feet
        public static readonly float GooThrowRange = O(nameof(GooThrowRange), 10f);
        public static readonly float GooThrowCooldown = O(nameof(GooThrowCooldown), 4f);
        public static readonly float GooKiteRange = O(nameof(GooKiteRange), 8f);

        // THE DEMON: it wanders and it throws spells, and it does neither on a
        // schedule anyone can read.
        public static readonly float DemonPatrolMin = O(nameof(DemonPatrolMin), 3f);
        public static readonly float DemonPatrolMax = O(nameof(DemonPatrolMax), 7f);
        public static readonly float DemonPatrolNear = O(nameof(DemonPatrolNear), 6f);
        public static readonly float DemonPatrolFar = O(nameof(DemonPatrolFar), 22f);
        public static readonly float DemonCastMin = O(nameof(DemonCastMin), 1.2f);
        public static readonly float DemonCastMax = O(nameof(DemonCastMax), 2.8f);
        // how far past a row's threshold its cast lands
        public static readonly float DemonCastPower = O(nameof(DemonCastPower), 2.2f);
        public static readonly float Lvl2AuraPeriod = O(nameof(Lvl2AuraPeriod), 0.8f); // seconds between aura beats
        public static readonly float UltimateRadius = O(nameof(UltimateRadius), 3.5f); // lvl3 area effects (flame burst, snow field…)
        public static readonly float UltimateSeconds = O(nameof(UltimateSeconds), 5f); // lifetime of lvl3 fields (plasma, inertia…)
        public static readonly float BarrierSeconds = O(nameof(BarrierSeconds), 5f); // two-way isolation duration
        public static readonly float DemonCooldown = O(nameof(DemonCooldown), 90f);    // cooldown between all-12 demon summons

        // ---- living particles ----
        public static readonly float ParticleKinRange = O(nameof(ParticleKinRange), 1.3f); // affinity range toward other particles
        public static readonly float ParticleChaseRange = O(nameof(ParticleChaseRange), 3.5f); // and toward nearby prey when no kin is around
        public static readonly float ParticleChaseAccel = O(nameof(ParticleChaseAccel), 1.7f); // gentler than kin-pull

        // ---- Spell particles (SPELL_PARTICLES.md - the matter-level law) ----
        public static readonly float SparkHeatDelta = O(nameof(SparkHeatDelta), 25f); // °C one spark/frost carries; 3 hits on one target = 3×
        public static readonly float ZoneEmitPeriod = O(nameof(ZoneEmitPeriod), 3.5f); // seconds between a zone's emissions (ONE particle per rune - law 10)
                                                                                       // (State conjures ONCE per activation instead)
        public static readonly int ParticleCap = Oi(nameof(ParticleCap), 120);        // world particle budget - oldest dies first
        public static readonly float ParticleLife = O(nameof(ParticleLife), 4.5f);    // seconds a particle lives (flames 2.5×, lvl2+ 1.5×)

        // ---- Pressure & explosion (density confined by rigid walls) ----
        public static readonly float PressureBuildRate = O(nameof(PressureBuildRate), 0.55f); // pressure/sec per unit gas intensity when fully contained
        public static readonly float ExplodeThreshold = O(nameof(ExplodeThreshold), 1f);      // contained pressure that triggers the burst
        public static readonly float ContainRange = O(nameof(ContainRange), 1.7f);            // a rigid surface within this counts as containing a side
        public static readonly float HeatPressureFactor = O(nameof(HeatPressureFactor), 0.6f); // Heat adds to the gas that pressurizes with Density
        public static readonly float ExplodeRadius = O(nameof(ExplodeRadius), 3.8f);
        public static readonly int ExplodeParticles = Oi(nameof(ExplodeParticles), 48);

        // ---- Persistent ink (characters & weapons) ----
        // a spent loop re-fires only after opening this wide and re-closing;
        // idle animation jiggle stays under it.
        public static readonly float ReArmDistance = O(nameof(ReArmDistance), 0.22f);
        public static readonly float ReCloseDistance = O(nameof(ReCloseDistance), 0.05f); // re-armed seals re-fire within this (forgiving, poses aren't millimetre-exact); fresh ink keeps the tight CloseThreshold
        public static readonly int MaxEnvironmentStrokes = Oi(nameof(MaxEnvironmentStrokes), 300);// oldest unsealed world ink fades beyond this (perf cap)

        /// One stroke closes on itself at exactly the distance two strokes
        /// link at. The parameter is deliberately ignored; kept for the call sites.
        public static float SelfCloseThreshold(float loopLength) => CloseThreshold;
    }
}
