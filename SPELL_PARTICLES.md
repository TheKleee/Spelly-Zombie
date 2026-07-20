# SPELL PARTICLES v2 — spec (Marko's design, 2026-07-08) — **BUILT same day**

Implementation: `Spells/SpellParticle.cs` (the law, ladders, lightning/laser/
shadow/void rift, visual fear), `Spells/Spell.cs` (zones = emitters),
`Matter.cs` (liquid bonds), `ZombieBrain.ScareVisible`, `DrawingConfig.cs`
(freeze mirror, particle dials). EffectMote.cs retired (unused, file kept).

**2026-07-19: GRAMMAR v4 (bottom of this file) is now the law of record.**
v2 remains the SUBSTRATE — attributes, donation, matter chemistry, liquid
bonds, fear layer, caps all keep working underneath ("old passive states are
kept") — but v4 REPEALS v2's annihilation rule and replaces the v3 pair
matrix entirely.

Everything a rune does = emit PARTICLES. Composition = collisions. No named
spells, no predetermined outcomes — mayhem from a small rulebook. Marko
vetoes/edits this doc, then it gets built exactly as written.

## THE LAW (v2 substrate — see v4 for what changed)

Every particle has a MATTER LEVEL and carries ATTRIBUTES. On collision:

1. **Different levels** → the lower (more ethereal) particle DISSOLVES into
   the higher (more material) one, donating its attributes.
2. **Same level** → ~~opposites ANNIHILATE~~ **REPEALED by v4: opposites now
   SYNTHESIZE** (v4 law 3); defined transmutations TRANSMUTE; everything else
   just COLLIDES like normal physics.
3. **Attribute thresholds transform particles** — v4 replaces the old
   threshold ladders with same+same LEVELING (v4 law 2).

| Level | Who | Notes |
|---|---|---|
| 0 | PUSH | pure force — donates velocity to anything |
| 1 | LIGHT, DARK | energy |
| 2 | DENSE, SPREAD, GLUE, REPEL | property carriers |
| 3 | SPARK, FROST | elemental matter |
| 4 | conjured SOLID/LIQUID Matter | real chemistry blobs |
| 5 | the world | creatures, surfaces, rigidbodies — absorb everything |

## ATTRIBUTES every particle carries

temperature · luminance (darkness = negative luminance) · density ·
stickiness · velocity/direction

**Density = WEIGHT (Marko's rule):** density attribute maps to gravity.
High → falls hard/sinks. Below air density → RISES/floats. Applies to
particles, matter blobs, and creatures (spread-hit zombie eventually
FLOATS — balloon zombie).

## Liquids = weak re-formable bonds (Marko's rule)

Liquid particles of the SAME material form weak BONDS with neighbors:
easily broken by force, re-formed on proximity — splash a puddle apart and
it re-pools. Bond strength IS the stickiness attribute:

- GLUE donated into a liquid → stronger bonds → slime/goo.
- REPEL/SPREAD donated → bonds shatter → mist/droplets.
- FREEZE → bonds lock rigid → the puddle becomes ONE solid (ice).
- MELT a solid → bonds loosen → liquid again (phase change = bond state).
- DIFFERENT materials never bond — chemistry decides instead
  (lava+water → stone+steam) or they simply coexist.

## Fear & perception layer (Marko's rule: zombies fear what they can SEE)

- Dangerous particles emit a FEAR signal — but only to creatures that can
  SEE them: vision cone + effective luminance above a visibility floor.
- **Invisible flame:** DARK donated into flame lowers its luminance below the
  floor → zombies walk straight into it. Darkness = the stealth layer.
- Blinded creatures fear nothing (can't see danger either — double-edged).

## Safety rails (inherited by v4)

- Global particle cap ~120 (oldest dies first), matter cap 90 stays.
- Particle life ~4s baseline; emitters re-emit every few seconds while the
  seal lasts (circle = 36s of periodic mayhem).

---

# GRAMMAR v4 — RATIFIED (2026-07-19, Marko's combination ruleset)

Marko: *"some spells felt useless and too weak on their own — adding more
combinations and rules sets the world for clear mayhem."*

Supersedes the v3 pair-matrix draft (removed; git history has it). Built as
a GRAMMAR, not a lookup table — new runes added after release get the whole
system for free (see EXTENSIBILITY).

## Roles — every rune is exactly one

| Role | Runes | Job |
|---|---|---|
| **ESSENCE** | HeatUp, HeatDown, Light, Darkness | WHAT the effect is (burn / freeze / judge / consume) |
| **FORM** | Solid, Liquid | The SHAPE — identity of the other ingredient is preserved ("considered X for further combinations") |
| **BEHAVIOR** | Sticky, Slick, Dense, Spread | HOW it acts: anchor · chaos · manifest · multiply |
| **VECTOR** | Arrow (away), Y (toward) | Pure motion. No effects of their own, ever. |

## The laws

1. **Universal attraction** — particles always try to combine with whatever
   particle is nearby, no matter the kind.
2. **Same + Same = level up.** lvl1+lvl1 = lvl2 (bigger, radiates its own
   effect around itself). base + lvl2 = **lvl3 ULTIMATE** — always an
   area/named form. Level cap: 3.
3. **Opposites SYNTHESIZE — annihilation is repealed.** Each pair declares a
   paradox product: Heat pair → BURNING STEAM (lvl2×lvl2 → HOT STEAM AREA:
   slow + burn) · Light+Dark → WHITE HOLE (pushes everything away) ·
   Sticky+Slick → uncontrollable grip (sticks to everything, can't be
   controlled or stopped) · Dense+Spread → **BARRIER**. Every future pair
   MUST ship its paradox product.
4. **Form preserves identity.** Solid X = tangible, carriable X. Liquid X =
   flowing, spreading X. The product still counts as X for further combining
   (liquid darkness + Dense = flowing dark matter).
5. **Behaviors modify.** Sticky = it STAYS / you can wield it (sticky lava,
   obsidian blade, light lure). Slick = it ESCAPES (random bolts, ragdoll,
   unstoppable). Dense = MANIFEST as a persistent object that merges with
   its own kind (Flame, Snowball, Glue, Dark matter). Spread = MULTIPLY into
   more, smaller copies → universal attraction chains them (lvl2 chance).
   **RULED: modifiers stack freely — "we'll just see what happens."**
6. **Mismatched levels combine normally** — the product is weaker by the
   weaker aspect (strength scales with the lower ingredient).
7. **Vectors.** Arrow+Arrow = **TORNADO** (swirls, lifts up, tosses out).
   Y+Y = **WHIRLPOOL** (swirls downward, drags, tosses out). Vector fields
   CARRY particles/compounds (and stamp their lineage — the finisher
   literally spins everything together). Vectors also STEER vector fields:
   an Arrow/Y hitting a tornado/whirlpool gives it travel direction.
8. **Lineage.** Every combination product carries the SET of ancestor rune
   types (bitmask; union on every combine). All 12 core runes in one chain →
   **THE DEMON** appears, however the chain got there.
   *Boundary rule (proposed Jul 20, veto freely): AREA fields are
   chain-TERMINAL — a snow field or plasma sun is where that chain ends —
   EXCEPT the carriers (tornado, whirlpool, liquid area), which keep and
   stamp lineage because carrying-things-onward is their whole identity.*
9. **Fallback.** Anything not covered above resolves by the v2 substrate
   (generic donation) — old passive states are kept.
10. **One particle per rune** (RULED Jul 19: "if you want 3, draw 3 runes") —
   the drawing IS the recipe. Powerups still add extras.
11. **Seal kin first** (RULED Jul 19): particles born of the same DRAWING
   prefer each other (2× attraction reach, strangers never beat family), so
   Light+Chill in one seal makes HEALING instead of the Lights finding each
   other and lightning-zapping the caster.
12. **THE SUSTAIN LAW** (RULED Jul 20, BUILT same day): a rune emits ONE
   particle and re-emits ONLY when it is fully gone — and "gone" follows
   combinations: the rune tracks what its particle BECAME (eater particle,
   field, matter blob, demon) and waits for the final product to disappear.
   One light rune = one light, forever — never an accidental lightning. A
   seal's steady state IS its recipe: Light+Light sustains exactly one
   lightning; Spark+Dense sustains one eternal flame (the torch seal).
   Powerup extras are untracked bonuses.

## Level ladders (same + same)

| Particle | lvl2 | lvl3 (base + lvl2) |
|---|---|---|
| HeatUp | bigger, burns things around it | **FLAME BURST** (area) |
| HeatDown | bigger, freezes things around it | **SNOW FIELD** (ice dmg + slow; stay too long → frozen) |
| Liquid | rapidly spreads | **LIQUID AREA** — floating bubble; you can drown; carries its liquid's effects |
| Solid | grows on its own, slowly | **SOLID AVALANCHE** — area spawns many lvl2 solids, growing and spreading |
| Light | **LIGHTNING** — ball, zaps random nearby targets | **PLASMA** — small sun: blinds, radiates heat, massive touch damage |
| Darkness | **BLACK HOLE** — pulls things in | **GROWING BLACK HOLE** — rapidly spreading area, sucks until it can't |
| Sticky | immovable; what it touches cannot move | **TIME FREEZE AREA** — stops anything inside |
| Slick | unstoppable, bounces, slips everything it touches | **INERTIA AREA** — nothing inside can stand still |
| Spread | spreads copies WITHOUT shrinking them | spreads copies LARGER than the original |
| Dense | larger + heavier | even larger + heavier |
| Arrow | TORNADO | (steerable by more vectors) |
| Y | WHIRLPOOL | (steerable by more vectors) |

## Cross matrix (Marko's rulings — implement via grammar + exotics table)

**HeatUp ×** Solid → **METEORITE** (of the solid's material; burning trail) ·
Liquid → liquid decides (water = hot steam lvl2 · magma = ERUPTION · sap =
spreading flaming liquid) · Darkness → **DARK FLAMES** (chase the shadows of
nearby moving targets — anything that moves) · Light → **SUN STRIKE**
(channeled, telegraphed, dodgeable by walking) · Sticky → **STICKY LAVA**
(slow + burn zone) · Slick → **FIRE BOLTS** (random, no targeting) ·
Spread → more sparks, smaller, chain-reaction lvl2 chance · Dense → **FLAME**
(persistent; flames merge into bigger flames).

**HeatDown ×** Solid → **ICE SPIKES** from the ground (freezing) · Liquid →
**GLACIER** (grows channeling, then frost explosion: knockover + freeze +
dmg) · Darkness → **ABSOLUTE ZERO** (instant freeze area) · Light →
**HEALING AREA** (heal over time — the ONLY healing in the game) · Sticky →
**OBSIDIAN BLADE** (pickable sharp weapon; lives spell duration + 5s) ·
Slick → **ICE BOLTS** (random, slow on hit) · Spread → more frost ·
Dense → **SNOWBALL** (merges bigger; passive area slow + chill).

**Liquid ×** Solid → liquid version of the solid (counts as SOLID; sheer
weight pushes and deals physical damage) · Darkness → dark liquid (counts as
DARKNESS; spreads) · Light → light liquid (counts as LIGHT; spreads) ·
Sticky → sticky liquid area (root/slow inside) · Slick → slip-ragdoll pool
(uncontrollable until you slide out) · Spread → spreads over the area ·
Dense → **PRESSURE JET** along the seal's normal.

**Solid ×** Darkness → solid darkness (carriable) · Light → solid light
(carriable) · Sticky → **STICKY SOLID** (stick things on it and carry them —
zombies and players included) · Slick → **SLICK SOLID** (hold it in front to
shove heavy things, players, zombies) · Spread → more solids ·
Dense → heavier, stronger solid.

**Darkness ×** Sticky → sticky darkness (attaches to things) · Slick → slick
darkness (spreads everywhere; blinds + ragdolls) · Spread → spreading dark
motes (may combine) · Dense → **DARK MATTER** (destroys other particles on
impact; slow; heavy damage).

**Light ×** Sticky → **STICKY LIGHT** (stationary; things stick to it;
ATTRACTS zombies — the lure) · Slick → slick light (random spread; blinds;
may ragdoll) · Spread → more light · Dense → **MULTIPLICATION** particle:
clones other particles on contact (ignores other multipliers — no infinite
loops); may clone ZOMBIES; on a PLAYER → **MIRROR IMAGE** (RULED): a copy
that repeats your movements offset nearby, casting your spells with you,
never separately controllable.

**Behavior × behavior:** Sticky+Slick → uncontrollable sticky chaos ·
Sticky+Spread → smaller glues · Sticky+Dense → **GLUE** (literally glues) ·
Slick+Spread → more slicks · Slick+Dense → **TELEPORT PAIR** (two linked
particles fly apart; whatever touches one teleports to the other's location)
· Spread+Dense → **BARRIER** (below).

**BARRIER (RULED):** two-way isolation. What's inside is protected from the
environment AND cannot act on it — a barriered zombie can't attack; no
particle combinations happen inside. Barrier is the system's insulator and
the only way to stop a chain (including an accidental Demon).

## THE DEMON (RULED)

- **Trigger:** one chain's lineage reaches all 12 core runes — any order,
  any route, however it happened.
- Large and powerful. **Unkillable — expires only with time.** Constantly
  summons random magical calamities around itself (random lvl2/lvl3 effects
  from the whole grammar). Zombies FEAR it unconditionally.
- Correct play: **run**, and hope it eats a lot of the horde.
- One Demon at a time. Post-release runes are NOT required for the Demon
  (Demon = the core 12) unless Marko flags a new rune as core.

## EXTENSIBILITY — why it's a grammar (the post-release rune contract)

- **RUNE REGISTRY:** every rune declares `{id, role, opposite, lineage bit,
  payload}`. Every law operates on ROLES — no law ever names a rune id.
- **A new rune ships with:** its registry entry, its role payload (essence
  effect / form wrap / behavior verb / vector), and its pair's paradox
  product. That's ALL — leveling, forms, behaviors, vectors, lineage,
  fallback all work for it on day one with zero engine changes.
- **EXOTICS TABLE:** sparse data overrides keyed by unordered rune pair
  (+ level tier) → named product (Sun Strike, Obsidian Blade, Healing Area,
  Teleport Pair, Multiplication…). The grammar computes the default result;
  the table wins where an entry exists. Data, not code branches — SigilSpell
  died for being an arbitrary table; this table only OVERRIDES a living
  grammar.
- Lineage mask sized by the registry (64-rune headroom).

## Build phases

1. **P1 — CORE (first test target):** registry · compound particles ·
   lineage · same+same leveling (lvl2/lvl3) · paradox synthesis
   (annihilation OFF) · Spread-multiply · Dense-manifest. Reuses existing
   FX/Matter/Thermal.
2. **P2 — FORMS: BUILT (Jul 19).** SpawnMatter is the RECIPE RESOLVER — the
   seal's rune list decides the conjure. Heat×Form: METEORITE (falls burning
   from 12m), ICE SPIKES (frozen ring, chills on touch), GLACIER (channels
   then frost-bursts), hot liquid by material (water→steam area · stone→
   ERUPTION · sap→spreading burning pour). Liquid+Dense=PRESSURE JET.
   Solid+Liquid=heavy ambient liquid of the solid. Sticky/Slick/Light/Dark
   forms: carriable lantern-solids, blinding dark matter-blocks, sticky
   carriers, slick plows. FORM LEVELING: State×2 = lvl2 (solid grows,
   liquid sheds spreading blobs), State×3 = SOLID AVALANCHE / LIQUID AREA
   (floating sea — wade or drown). Matter carries LINEAGE (a block can
   complete the all-12 Demon chain) and cold matter now chills on contact
   (mirror of lava).
3. **P3 — BEHAVIOR CROSSES + EXOTICS:** sticky/slick per essence · barrier ·
   teleport pair · multiplication + mirror image · obsidian blade · healing
   area · sun strike · dark flames · absolute zero · dark matter.
4. **P4 — VECTORS: BUILT (Jul 19).** Push emits slower (6 m/s), seeks ONLY
   other Pushes (vectors love vectors); Arrow+Arrow → TORNADO, Y+Y →
   WHIRLPOOL (polarity read from lineage bits; mixed arrow+Y pools,
   undefined on purpose). The storm carries particles + stamps its lineage,
   a thrown Arrow/Y STEERS it, and it TOSSES everything out when it dies.
   Zone push on objects cut to 0.08× (arrows do nothing on their own);
   player feet-seal flight kept.
5. **P5 — THE DEMON: BUILT (Jul 19).** All-12 lineage → Demon.SummonGrand:
   LARGE (2.8×), UNKILLABLE (health refreshed every frame — only its 42s
   clock ends it), casts a random calamity from the whole grammar every
   2-3.5s (flame bursts, plasma, black/white holes, time freeze, tornados,
   meteors…), unconditional zombie terror in 22m. It still eats elements
   and transmutes. One at a time, 90s cooldown. Correct play: RUN.

**FX OVERRIDES (Marko's control):** every particle and field look is
code-generated primitives — replace any of them by dropping a prefab into
`Resources/Custom`: `FX_Spark`, `FX_Frost`, `FX_Flame`, `FX_Lightning`,
`FX_BlackHole`, `FX_Glue`… (one per ParticleKind) and `FX_SnowField`,
`FX_PlasmaField`, `FX_HealingField`, `FX_TornadoField`, `FX_LiquidAreaField`…
(one per field class). The code sphere/dome hides; your prefab rides the
effect. Boundary rings + inside-HUD stay code-side.

## Open TBDs for Marko

1. ~~Solid lvl3~~ RULED: SOLID AVALANCHE. ~~Darkness lvl3~~ RULED: GROWING
   BLACK HOLE. ~~White hole magnitude~~ RULED: same strength as black hole,
   opposite direction.
2. Barrier duration when it lands on a zombie.
3. Should a TEAMMATE's hand trigger your body seal? (limb re-cast is
   currently same-body only; dropped weapons have no limb trigger at all.)
