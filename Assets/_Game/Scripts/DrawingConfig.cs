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
        public static readonly float NodeSpacing = O(nameof(NodeSpacing), 0.007f);   // min world distance between nodes. Marko Jul 31: "the runes keep not knowing they are connected - shorten the distance of node creation." Sparse nodes leave real gaps between ink that visually touches, so touch tests miss and one rune reads as several.
        public static readonly float SurfaceOffset = O(nameof(SurfaceOffset), 0.008f); // lift ink off the surface to avoid z-fighting
        public static readonly float MaxStrokeJump = O(nameof(MaxStrokeJump), 0.12f); // hit point jumping further than this in one frame ends the stroke — separate marks STAY separate
        public static readonly float MaxStrokeJumpPerMeter = O(nameof(MaxStrokeJumpPerMeter), 0.02f); // tiny distance allowance (fast flicks split; forgiving seals reconnect what should connect)
        public static readonly int MinStrokeNodes = Oi(nameof(MinStrokeNodes), 2);   // Marko Jul 31: "lines aren't even created when I don't move the mouse far enough". 3 nodes = ~3cm of travel before ANY line existed — and an arrowhead's barb or a LIGHT ray is exactly that short. A limb is not an accidental click; 2 is the minimum that can be a line at all.
        public static readonly float InkWidth = O(nameof(InkWidth), 0.007f);         // line renderer width — THIN pen (Marko: more runes in smaller places)
        public static readonly float DrawSmoothingTime = O(nameof(DrawSmoothingTime), 0.025f); // hand-jitter smoothing time constant, seconds (0 = raw input)
        public static readonly float DrawLookSensitivityScale = O(nameof(DrawLookSensitivityScale), 0.35f); // camera sensitivity multiplier while the pen is down
        public static readonly float EraseRadius = O(nameof(EraseRadius), 0.02f);
        // ---- evaporation (Marko Aug 5: "the longer the game lasts the more it
        // lags - old ink standing there unused should evaporate after 1 minute") ----
        // ---- rune emoji sitting inline in a line of text (TMP sprites sit on
        // the baseline; letters read centred higher, so the icon needs a lift) ----
        public static readonly float RuneIconLift = O(nameof(RuneIconLift), 0f);     // em above the baseline. 0 = no tag emitted; the sprite asset's own metrics do the aligning
        public static readonly float RuneIconScale = O(nameof(RuneIconScale), 100f); // % of the surrounding text size. 100 = no tag emitted
        public static readonly float InkEvaporateSeconds = O(nameof(InkEvaporateSeconds), 60f);    // loose world ink lives this long
        /// THE SCOOP TAX (Marko Aug 8): rubbed-out ink refills the wand at a LOSS —
        /// "without that logic people could cast spells for free without having to
        /// take any ink from the cauldron ever." Half back is his opening number
        /// ("or 70% if half is too strict idk"), so it lives in sz_tuning.json for
        /// him to try 0.7 without a build. He wants wizard power LIMITED.
        public static readonly float ScoopRefund = O(nameof(ScoopRefund), 0.5f);
        /// How long a WORLD seal lives after casting before its ink is consumed
        /// (Marko Aug 8: "consumed quickly after the spell is cast" — no re-emit
        /// farming, no erase-and-recast). Body seals keep SealProduceSeconds.
        public static readonly float SealConsumeSeconds = O(nameof(SealConsumeSeconds), 1.2f);

        // ---- THE POT (Marko's cauldron economy, ruled Aug 8-9). One ink pool,
        // no exceptions: every wand refill bills it, nothing ever refills it.
        public static readonly float PotPrepSeconds = O(nameof(PotPrepSeconds), 30f);        // inert gather phase, then it opens full
        public static readonly float PotCapacityInk = O(nameof(PotCapacityInk), 1600f);      // total reserve, in wand ink units
        public static readonly float PotCloseRadius = O(nameof(PotCloseRadius), 2.6f);       // "close enough to lift it": fast refill, spill, defuse, corrupt touch
        public static readonly float PotRefillRange = O(nameof(PotRefillRange), 45f);        // beyond this the refill sits at the floor rate
        public static readonly float PotRefillNearPerSec = O(nameof(PotRefillNearPerSec), 45f);  // ink/s at the pot — "refill way quicker" (Aug 10): a full tank in ~2s standing over it
        public static readonly float PotRefillFloorPerSec = O(nameof(PotRefillFloorPerSec), 2.5f); // ink/s across the map — never truly dry, never enough to camp on
        public static readonly float PotSpillPerSec = O(nameof(PotSpillPerSec), 8f);         // full wand inside the close radius: the tap keeps running, the pot pays
        public static readonly float PotCorruptDrainPerSec = O(nameof(PotCorruptDrainPerSec), 11f); // green evaporation, "as if 3 or 4 people are filling their wands"
        public static readonly float PotAcolyteFillPerSec = O(nameof(PotAcolyteFillPerSec), 9f);   // the babysitting tax: their corruption FILLS it
        public static readonly float PotAcolyteFillRadius = O(nameof(PotAcolyteFillRadius), 6f);   // must stay smaller than a sensible overwatch distance
        public static readonly float PotCorruptSeconds = O(nameof(PotCorruptSeconds), 3.2f); // acolyte touch to turn it green — the CS PLANT time (his ruling)
        public static readonly float PotDefuseSeconds = O(nameof(PotDefuseSeconds), 10f);    // wizard presence to turn green back — the CS DEFUSE time (colour only, ink lost is lost)

        // ---- THE STRIKE (Marko Aug 9: "spells need to be faster and jump to
        // move towards the target... when I cast a fire I need to feel like a
        // god"). Delivery only — the elemental chemistry is untouched.
        public static readonly float StrikeLockRange = O(nameof(StrikeLockRange), 12f);   // snapshot at spawn + hover lock-on radius
        public static readonly float StrikeSpeed = O(nameof(StrikeSpeed), 14f);           // the slam. 24 flew PAST everything ("way too fast and extremely not precise")
        public static readonly int StrikeBurstPieces = (int)O(nameof(StrikeBurstPieces), 4f); // shatter count on impact — the flying debris that teaches grabbing
        /// Scan ink per meter of the scanned object's largest dimension (Marko:
        /// "larger objects... regenerate more ink" — size IS the reward, and a
        /// big disguise is a bad disguise, so the trade balances itself).
        public static readonly float ScanInkPerMeter = O(nameof(ScanInkPerMeter), 25f);
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

        // ---- Seal shape -> WHICH VARIANT, and a flat production time ----
        // Marko, Aug 6: "Seal sides create variants... different types of the same
        // thing. Like we could create different zombies (smarter and dumber for
        // example) per rune." And: "All seals keep producing for 10 seconds."
        //
        // SIDE COUNT IS A SELECTOR, NOT A MAGNITUDE. It picks which variant of that
        // rune's spell you get. It sets no duration and no lifetime. The old
        // 1-second-per-side rule (Jul 22) is DEAD; it survived in code long after
        // he had replaced it, and it is what made Claude keep repeating it back.
        public static readonly int CircleEdges = Oi(nameof(CircleEdges), 10);        // a circle reads as 10 sides = the top variant
        public static readonly float SealProduceSeconds = O(nameof(SealProduceSeconds), 10f); // FLAT. Every seal, every shape.
        public static readonly float CircleMaxVariance = O(nameof(CircleMaxVariance), 0.16f);// radius variation below this = circle — HAND circles are 10-18% wobbly, 7% only accepted machines
        public static readonly int CircleMinCorners = Oi(nameof(CircleMinCorners), 8); // and it must not be an obvious low-corner polygon
        public static readonly float RdpEpsilonFactor = O(nameof(RdpEpsilonFactor), 0.015f);// RDP epsilon as fraction of the loop's bounding diagonal
        public static readonly float MinCornerAngle = O(nameof(MinCornerAngle), 20f); // degrees of direction change required to count as an edge corner

        // ---- Zombie navigation (Marko Aug 6: "Zombies need a way to navigate.
        // They are naturally stupid but they need some navigation logic at least
        // simple kind.") ----
        // NO NAVMESH ON PURPOSE. Baking would have to happen per map, and maps are
        // the content strategy: downloaded environments must work with zero prep.
        // NavMeshAgent also owns its own movement, which fights the Rigidbody that
        // spells push, freeze and ragdoll. So: local steering, and wall-following
        // when steering alone cannot escape (a courtyard with one exit).
        public static readonly float ZombieLookAhead = O(nameof(ZombieLookAhead), 1.8f);      // metres probed in front. Bigger = smarter = notices walls sooner
        public static readonly float ZombieProbeRadius = O(nameof(ZombieProbeRadius), 0.35f); // half a zombie's width, so it fits where it thinks it fits
        public static readonly float ZombieStuckSeconds = O(nameof(ZombieStuckSeconds), 0.9f);// trying to move but going nowhere for this long = start wall-following
        public static readonly float ZombieWallFollowSeconds = O(nameof(ZombieWallFollowSeconds), 3f); // how long to commit to hugging one side before trying straight again

        // A summoned zombie is a SPELL and expires. Marko: "zombies should not
        // last forever... they will last a duration forcing acolytes to create
        // more zombies and reveal themselves" and "zombies will last for a minute
        // at least. Cause they need to walk." NOT the same clock as the seal,
        // which produces for a flat 10 seconds; what it produced lives its own life.
        public static readonly float SummonedZombieLife = O(nameof(SummonedZombieLife), 75f);

        /// Pupils of a zombie somebody else is steering. Red reads as "not its
        /// own idea" instantly, and it is the only red on a creature otherwise
        /// made of greens.
        public static readonly Color MindControlEyeColor = new Color(0.85f, 0.10f, 0.10f, 1f);

        // ---- how big a zombie is, and how big its eyes are ----
        // Marko, Aug 6: "They are too large... They should be more of a player
        // size and they have huge eyes". Both are numbers rather than prefab
        // surgery, so he can tune them live in sz_tuning.json and never risk the
        // animation by rebuilding a prefab.
        // How long the green puff off a dead zombie lingers. The detonation
        // cloud, when it is built, is the loud one — this is the quiet version.
        public static readonly float ZombieDeathCloudSeconds = O(nameof(ZombieDeathCloudSeconds), 2.2f);

        public static readonly float ZombieBodyScale = O(nameof(ZombieBodyScale), 0.82f); // multiplies KindScale. 1 = the old size
        public static readonly float ZombieEyeScale = O(nameof(ZombieEyeScale), 1.3f);    // was a hardcoded 2.2, which is where the saucers came from

        // ---- THE THREE DIALS, ZOMBIE READING (Marko Aug 10) ------------------
        // He had to state these twice because the first build crossed two of
        // them: the rune-to-seal RATIO was driving body size, when body size
        // belongs to the seal's OWN ABSOLUTE SIZE and the ratio belongs to the
        // gas. They are independent axes and must stay that way.
        //
        // ONLY THE TWO ENDS ARE AUTHORED (Marko Aug 10: "only smallest and
        // largest should be marked other things are derived"). Everything
        // between is interpolated, so "normal" is a CONSEQUENCE of the ends
        // rather than a third number that can drift out of agreement with them —
        // which is exactly what a midpoint anchor PLUS clamps was: three numbers
        // describing two facts.
        //
        // DIAL 1 — SEAL DIAMETER → BODY SIZE. Absolute and linear between the
        // ends, continuous the whole way: a centimetre wider seal is always a
        // measurably taller zombie, never a jump to the next preset.
        //
        //   8cm scribble  → 0.25m  SCOUT — his ruling, and it is a ROLE, not a
        //                          stat block: "tricky scouts to not be as
        //                          easily noticed as larger ones"
        //   1.8m circle   → 5.4m   GIANT
        //   (~0.55m chair-width falls out at ~1.66m, his "normal zombie")
        // TEN, AND THE KEYBOARD IS THE REASON (his ruling): 1-9 then 0 address
        // every zombie in the first-person overwatch. THE CAP IS THE UI.
        public static readonly int AcolyteZombieCap = Oi(nameof(AcolyteZombieCap), 10);

        // THESE FOUR ARE REFERENCE POINTS, NOT LIMITS (Marko Aug 10: "should we
        // even force a limit upon them? what if someone manages to draw across
        // half a map? Why not be rewarded for it?... this is supposed to still
        // be a funny friend slop game"). They mark two points and the line runs
        // THROUGH them and keeps going in both directions. Draw a circle around
        // the whole village and you get the kaiju you earned; draw a seal the
        // size of a coin and you get a zombie the size of a bug.
        // RE-RULED DIAL 2 (Marko Aug 11 evening): body size follows the RUNE'S
        // own drawn diameter, not the seal's. These anchor the line: a rune
        // this small → SummonSizeMin, this big → SummonSizeMax, unclamped
        // beyond both. Defaults assume a rune fills roughly half its seal, so
        // yesterday's drawings raise roughly yesterday's zombies.
        public static readonly float SummonRuneMin = O(nameof(SummonRuneMin), 0.04f);
        public static readonly float SummonRuneMax = O(nameof(SummonRuneMax), 0.9f);
        public static readonly float SummonSealMin = O(nameof(SummonSealMin), 0.08f); // this seal...
        public static readonly float SummonSizeMin = O(nameof(SummonSizeMin), 0.152f); // ...makes this zombie (0.25m scout)
        public static readonly float SummonSealMax = O(nameof(SummonSealMax), 1.8f);   // and this seal...
        public static readonly float SummonSizeMax = O(nameof(SummonSizeMax), 3.3f);   // ...makes this one (5.4m giant)

        // THE ONLY REAL FLOOR, AND IT IS PHYSICS NOT BALANCE. Unity's solver
        // tunnels colliders under roughly 1-2cm straight through the ground, so
        // below this a zombie does not get funnier, it silently falls out of the
        // world. 0.012 is a ~2cm zombie: an ant you have to squint at, which is
        // the small end taken as far as it can actually go.
        public static readonly float SummonSizeFloor = O(nameof(SummonSizeFloor), 0.012f);


        // DIAL 2 — RUNE SIZE RELATIVE TO THE SEAL → GAS RADIUS, on the same
        // two-ends-only rule. The ratio is already 0..1 (a speck in a wide loop
        // → a rune filling its seal), so it interpolates straight between these.
        // Every zombie carries a small cloud while it lives (his reference: the
        // WARCRAFT 3 ABOMINATION's disease cloud), the death puff uses the same
        // radius, and DETONATION IS ALWAYS 3x.
        public static readonly float SummonGasRadiusMin = O(nameof(SummonGasRadiusMin), 0.4f);
        public static readonly float SummonGasRadiusMax = O(nameof(SummonGasRadiusMax), 2.5f);
        // THE LIVING AURA, as a fraction of body height. Deliberately TIGHT —
        // the Abomination's disease cloud hugged its unit. A zombie has to be
        // approachable or "their role is to be chased" stops being true, and the
        // big cloud waits for its corpse. Raise this and zombies go back to
        // being walking area denial.
        public static readonly float PoisonAuraBodyMul = O(nameof(PoisonAuraBodyMul), 0.6f);

        public static readonly float SummonGasDetonateMul = O(nameof(SummonGasDetonateMul), 3f); // "always 3x larger"
        // THE POISON, as a zone (PoisonField). Damage is PER SECOND and the
        // field bills it by real dt, so it stays under the 5-damage line that
        // shakes his camera — a DoT that shook the view made drawing impossible.
        public static readonly float PoisonDamage = O(nameof(PoisonDamage), 9f);

        // METRES → FX SCALE for the cloud sprites. The CFXR poison prefab emits
        // 2-3 UNIT particles from a point, so the honest metre radius the damage
        // uses has to be converted or a small cloud fills the screen. Purely a
        // LOOK dial — it does not change how far the gas reaches.
        public static readonly float PoisonFxScale = O(nameof(PoisonFxScale), 0.5f);

        // How often a poison zone releases ONE puff. Never a burst: FxLibrary
        // drops everything past 8 spawns in a frame, so a field that asked for a
        // handful at once rendered as nothing. At this cadence against a ~2.8s
        // puff life, roughly ten clouds are alive at any moment and the zone
        // fills without ever spiking the budget. LOWER = thicker fog.
        public static readonly float PoisonPuffEvery = O(nameof(PoisonPuffEvery), 0.28f);

        // A HORDE MUST NOT COST A HORDE OF PARTICLES. Above this many poison
        // zones the cadence stretches in proportion, holding total smoke roughly
        // constant whether one zombie is breathing or forty are. FxLibrary pools
        // only 12 instances per prefab and destroys the overflow, so concurrent
        // COUNT is the thing that has to be capped — sprite size is not the cliff.
        public static readonly float PoisonFxCrowd = O(nameof(PoisonFxCrowd), 6f);
        // and smoke nobody can see is never spawned at all
        public static readonly float PoisonFxDistance = O(nameof(PoisonFxDistance), 30f);

        // IT CLINGS AND IT GROWS (his spec): standing in poison leaves a small
        // cloud on your HEAD that others can see, it swells the longer you stay
        // in, and it fades on its own once you are out — the world stabilising.
        // While you wear it, YOU poison everyone else you walk past.
        public static readonly float PoisonClingRadius = O(nameof(PoisonClingRadius), 0.7f);
        public static readonly float PoisonClingGrow = O(nameof(PoisonClingGrow), 0.55f); // metres per second of exposure
        public static readonly float PoisonClingMax = O(nameof(PoisonClingMax), 3.2f);
        public static readonly float PoisonClingSeconds = O(nameof(PoisonClingSeconds), 4f);

        // THE DETONATION. Both scale with the seal's line count (potency), so a
        // triangle drawn on a zombie really is the potent one. Shove is the
        // headline — "how you steal the cauldron" — and the damage is the tax
        // for standing next to it, not the point of it.
        // Impulse strong enough to knock you OUT of draw mode and into first
        // person, so the shove lands on a body free to move. Below this a knock
        // leaves you drawing — the easel anchor still does its job. Detonation
        // shoves at 30 in the middle and falls off to 0 at the rim, so this is
        // also "how close you must be for the blast to break your concentration".
        public static readonly float ShoveBreaksDrawing = O(nameof(ShoveBreaksDrawing), 8f);

        public static readonly float DetonateShove = O(nameof(DetonateShove), 30f);
        public static readonly float DetonateDamage = O(nameof(DetonateDamage), 14f);

        public static readonly float DetonateFieldSeconds = O(nameof(DetonateFieldSeconds), 8f);
        // blast RADIUS floor as a multiple of body height, so the diameter is
        // never under 3x the zombie that blew up, whatever its rune said
        public static readonly float DetonateBodyMul = O(nameof(DetonateBodyMul), 1.5f);
        // how far to look for a zombie under a closing seal...
        public static readonly float DetonateSealReach = O(nameof(DetonateSealReach), 1.1f);
        // ...and how close to its SURFACE the seal must sit to count as drawn ON
        // it. Ink lies on the skin, so this is small on purpose: a seal on the
        // cobbles beside a zombie is still a summon.
        public static readonly float DetonateSurfaceSlack = O(nameof(DetonateSurfaceSlack), 0.25f);

        // DIAL 3 — FEWER SEAL LINES → STRONGER. 1.2x per line MISSING from ten,
        // so 1.2^(10 - Edges): triangle 3.58x, 9 sides 1.2x, and a circle (Edges
        // == CircleEdges == 10) is exactly 1.0 — "drawing a circle will make them
        // as powerful as they look (based on size)". The circle is the honest
        // baseline; every line you SAVE is the skill reward.
        public static readonly float SealLineBonus = O(nameof(SealLineBonus), 1.2f);

        /// How far along the arrow a commanded zombie marches. An arrow is a
        /// HEADING, not a map pin: it points, they walk that way until they get
        /// there or until something more interesting happens on the way.
        public static readonly float ZombieMarchDistance = O(nameof(ZombieMarchDistance), 25f);

        /// Total arc a Y fans its zombies across. The arrow sends a column at one
        /// spot; the Y sweeps a front. 110 degrees is wide enough to read as
        /// "spread out" without sending anyone backwards.
        public static readonly float ZombieScatterArc = O(nameof(ZombieScatterArc), 110f);

        /// Summoned dead are GREEN, because they are made of corrupt ink like
        /// everything else an acolyte touches. Marko: melee brownish green, ranged
        /// light green, so you can read which one is coming at you from across a
        /// courtyard without a single icon.
        public static readonly Color SummonMeleeColor = new Color(0.40f, 0.44f, 0.22f, 1f);
        public static readonly Color SummonRangedColor = new Color(0.58f, 0.80f, 0.46f, 1f);

        // ---- Zombie nerve (Marko Aug 6) ----
        // "Zombies are really weak and are easily destroyed but are also really
        // afraid... and they stop running away if player isn't chasing them
        // (isn't nearby)." Fear is REACTIVE, not a standing state: they flee who
        // is coming for them, and get on with it when nobody is.
        // "Also zombies attack players that turned back to them... so that they
        // can more easily protect their masters, or draw more attention to
        // themselves... they want to be chased, that's their role."
        public static readonly float ZombieChaseRange = O(nameof(ZombieChaseRange), 7f);  // a player nearer than this is "on me"; further and I stop panicking
        public static readonly float ZombieBackAngle = O(nameof(ZombieBackAngle), 105f);  // player facing more than this far from me = their back is turned

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
        // The HAND throw (E). Was const 22 ("twice as strong", Aug 9); Aug 11:
        // "a bit underwhelming... 1.5x might be the sweet spot or 2x, need to
        // test" — 33 here, put ThrowSpeed: 44 in sz_tuning.json to feel 2x.
        public static readonly float ThrowSpeed = O(nameof(ThrowSpeed), 33f);
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
        public static readonly float ZoneRadiusScale = O(nameof(ZoneRadiusScale), 1.6f); // SIZE dial: particle size seed = drawn rune size × this
        // EFFECT-RADIUS dial (Marko Aug 11 re-rule): zone reach = (rune size /
        // seal radius) × this. Ratio ~0.5 is a typical rune in a typical seal,
        // so 1.0 here keeps yesterday's reach for yesterday's drawing.
        public static readonly float RuneReachScale = O(nameof(RuneReachScale), 1.0f);
        // DORMANT SPELLS (Marko Aug 11, superseding caster-grace the same day:
        // "the user doesn't need immunity at all"): ground seals cast frozen
        // hologram previews at this fraction of true size; anything thrown or
        // released turns live after the universal delay — safety is a property
        // of the THING, never of a person. An untouched preview politely
        // fades out after its lifetime.
        public static readonly float DormantPreviewScale = O(nameof(DormantPreviewScale), 0.4f); // his Aug 11 tune: 0.2 read too small
        public static readonly float WakeDelaySeconds = O(nameof(WakeDelaySeconds), 0.2f);
        public static readonly float DormantLifeSeconds = O(nameof(DormantLifeSeconds), 45f);
        // A preview with purpose (his priority order: enemy > ally-in-need >
        // sleeping kin > hold): how far it senses, and how fast it drifts.
        public static readonly float DormantSeekRange = O(nameof(DormantSeekRange), 7f);
        public static readonly float DormantSeekSpeed = O(nameof(DormantSeekSpeed), 3.4f); // his tune: combine way faster
        // How far off its seal's surface a preview hovers, along the seal's
        // normal — walls push previews OUT, floors push UP, slants slant.
        // Marko, final: "not hover too far away from the seal... I'd rather
        // look down than up" — waist height, one uniform ceiling for every
        // floor preview (a hard clamp enforces it whatever physics tries).
        public static readonly float DormantHoverRange = O(nameof(DormantHoverRange), 0.7f);
        // Birth ritual: a fresh zombie plays its StandUp climb and the brain
        // holds still this long — match it to the clip's real length.
        public static readonly float ZombieRiseSeconds = O(nameof(ZombieRiseSeconds), 2f);
        // The meteor's terminal dive (his "it should fall really fast").
        public static readonly float MeteorFallSpeed = O(nameof(MeteorFallSpeed), 32f);
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
        public static readonly float InkCostPerMeter = O(nameof(InkCostPerMeter), 11f); // Marko Jul 30: "the wand is really slowly becoming shorter - it needs to lose ink much faster" (was 6/m ≈ 16m per tank; now ≈ 9m, and the lobby cauldron refills forever)
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
        /// CORRUPT ink is GREEN (his standing law: a corrupt cauldron makes a green
        /// wand). An acolyte holds nothing else, so their wand is green outright;
        /// a corrupting wizard's greens from the tip down as the soul eats it.
        public static readonly Color CorruptInkColor = new Color(0.25f, 0.62f, 0.16f, 1f);
        public static readonly float InkPerKill = O(nameof(InkPerKill), 10f);        // shared to ALL players per zombie down — the fast lane
        // ---- THE WAND ECONOMY (Marko Aug 10: "they should evaporate way quicker
        // and refill way quicker as well") ------------------------------------
        //
        // WIZARD  — no passive anything. The pot is the only well, poured by
        //           CauldronEconomy.LocalWandTick, fast up close and a trickle
        //           far away. Losing the pot means losing your regen entirely.
        // ACOLYTE — no pot at all. Ink EVAPORATES and comes back only from
        //           scanning, so standing still is the one thing they cannot do.
        //
        // DEAD: InkRegenPerSec used to be Awarded every frame to BOTH sides from
        // PlayerInk.Update, on top of the cauldron. Nothing reads it now.
        public static readonly float AcolyteInkEvaporatePerSec = O(nameof(AcolyteInkEvaporatePerSec), 1.6f);

        // ⛔ REVERSED Aug 11: "it's boring if we start the game with no wands...
        // so let them start with wands." Starting empty was his own Aug 9 rule
        // and it lost on FEEL — the first thing a player does is stand around
        // unable to play while they go find ink. Losing your wand still means
        // something; being handed an empty one to begin with just delays the game.
        //
        // A FRACTION of the ceiling rather than a flat number, so it tracks
        // InkMax (and the Drawing perk's larger well) instead of drifting from
        // it. 0 restores the wandless start if he ever wants it back for a mode.
        public static readonly float StartInkFraction = O(nameof(StartInkFraction), 1f);

        // ---- THE WAND TIP FLOW: his dowsing rod ------------------------------
        // Mote size rides the RATE of ink change, not just its direction, which
        // makes one effect answer two questions: am I gaining or losing (which
        // way they fly) and how fast (how big they are). Because the cauldron
        // refills faster the closer you stand, "how big" IS the hot-cold compass
        // to a hidden pot — no second system, nothing new to teach.
        //
        // Rates are in ink FRACTION per second. For scale: the pot up close is
        // ~0.45, drawing drains ~0.2, the far-away floor is ~0.025, and an
        // acolyte's evaporation is ~0.016.
        public static readonly float WandFlowDeadzone = O(nameof(WandFlowDeadzone), 0.008f); // below this the tip is quiet
        public static readonly float WandFlowFullRate = O(nameof(WandFlowFullRate), 0.25f);  // at or above this the motes are at full size
        public static readonly float WandMoteMin = O(nameof(WandMoteMin), 0.005f);           // world metres — a trickle
        public static readonly float WandMoteMax = O(nameof(WandMoteMax), 0.026f);           // world metres — standing in the cauldron

        // The POT speaks the same language on its own scale: rising motes mean
        // it is evaporating (the acolytes' win creeping closer), motes falling
        // in mean an acolyte standing too near is refilling it from their own
        // corruption. Pot fill moves far slower than a wand, hence its own rates.
        public static readonly float PotFlowDeadzone = O(nameof(PotFlowDeadzone), 0.0006f);
        public static readonly float PotFlowFullRate = O(nameof(PotFlowFullRate), 0.02f);

        // Zombies loitering at a black pot corrupt it too — "many times slower
        // than the actual Acolyte" (Aug 11). 0.12 ≈ 8x slower: a plant by horde
        // takes ~27s of undisturbed loitering vs the acolyte's ~3.2, long
        // enough that the creeping green gives wizards a fair alarm.
        public static readonly float PotZombieCorruptFactor = O(nameof(PotZombieCorruptFactor), 0.12f);

        // THE FLEE (Marko Aug 11, superseding the same-day timed hop: "we
        // essentially remove the timer and leave it to strategy instead"): ink
        // moves ONLY when its pot is shattered — beam up, this many seconds of
        // drought with no wizard regen anywhere, then beam down + splash in a
        // random SURVIVING pot. Pool and corruption ride along unchanged.
        // Broken stays broken; the last break grounds the ink at the InkGrave.
        public static readonly float PotHopGapSeconds = O(nameof(PotHopGapSeconds), 10f);

        // THE LOBBY FAILSAFE (Marko Aug 11): the lobby pot is the REAL pot —
        // it drains visibly, corrupts, defuses — but every this-many seconds it
        // rebrews itself full and black. Practice with real behaviour, stakes
        // with a net. "It cannot be deprived... but you can see it depriving."
        public static readonly float LobbyPotRefreshSeconds = O(nameof(LobbyPotRefreshSeconds), 10f);

        // THE PAINT FREEZE (Marko Aug 11): ink touching a zombie pins it in the
        // rest pose — the pose its paint shell describes — for this long past
        // the last touch. Long enough to lift the pen between a seal's strokes
        // without the canvas walking off; short enough that a single dab is a
        // tease, not a stun-lock.
        public static readonly float ZombiePaintFreezeSeconds = O(nameof(ZombiePaintFreezeSeconds), 2.5f);

        // How long a broken lobby prop stays gone before rebuilding itself.
        // Long enough that breaking things still reads as breaking them, short
        // enough that the courtyard is never stripped of things to scan.
        public static readonly float LobbyRespawnSeconds = O(nameof(LobbyRespawnSeconds), 15f);

        // THE METEOR (Marko Aug 10): born at the seal, thrown up, swelling as it
        // climbs, then plain gravity — "this way we have a lot more visual
        // clarity of what's going on". Growth is what answers "meteor is quite
        // small": the size lands EARNED instead of assigned.
        public static readonly float MeteorRiseSpeed = O(nameof(MeteorRiseSpeed), 13f);
        public static readonly float MeteorGrowth = O(nameof(MeteorGrowth), 3.5f);   // final size vs birth size
        public static readonly float MeteorGrowSeconds = O(nameof(MeteorGrowSeconds), 0.9f);

        // Fusions ADD their parents' drawn size (SpellParticle.FuseSize) — his
        // "combinations are additions so they increase really quickly". This is
        // the ceiling on a long chain, high enough that stacking still feels
        // explosive and low enough that a runaway loop cannot produce a spell
        // the size of the map by accident.
        public static readonly float FusedSizeCap = O(nameof(FusedSizeCap), 12f);

        // THE NEUTRAL POINT. The smallest size a rune can carry — the floor of
        // z.Radius in Spell.cs — so SpellParticle.SizeMul returns exactly 1 here
        // and a smallest-rune spell stays byte-for-byte what he already tuned.
        // ONE constant, so the floor and the reference can never drift apart.
        public static readonly float RuneSizeMin = O(nameof(RuneSizeMin), 0.9f);
        // and the ceiling on how far a fused spell may outgrow that neutral
        public static readonly float FusedSizeMulMax = O(nameof(FusedSizeMulMax), 3f);

        // Drain WHILE THE PEN IS DOWN, on top of the per-metre cost below — so
        // hovering a live pen bleeds you even when the line is short. Was a
        // hardcoded const 6 inside WandState, the one drain sz_tuning could not
        // reach.
        public static readonly float WandDrainPerSec = O(nameof(WandDrainPerSec), 14f);
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
