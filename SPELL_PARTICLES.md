# SPELL PARTICLES v2 — spec (Marko's design, 2026-07-08) — **BUILT same day**

Implementation: `Spells/SpellParticle.cs` (the law, ladders, lightning/laser/
shadow/void rift, visual fear), `Spells/Spell.cs` (zones = emitters),
`Matter.cs` (liquid bonds), `ZombieBrain.ScareVisible`, `DrawingConfig.cs`
(freeze mirror, particle dials). EffectMote.cs retired (unused, file kept).

Everything a rune does = emit PARTICLES. Composition = collisions. No named
spells, no predetermined outcomes — mayhem from a small rulebook. Marko
vetoes/edits this doc, then it gets built exactly as written.

## THE LAW (Marko's matter-level rule — replaces the v1 collision matrix)

Every particle has a MATTER LEVEL and carries ATTRIBUTES. On collision:

1. **Different levels** → the lower (more ethereal) particle DISSOLVES into
   the higher (more material) one, donating its attributes.
2. **Same level** → opposites ANNIHILATE (light+dark; spark+frost → steam
   poof); defined transmutations TRANSMUTE; everything else just COLLIDES
   like normal physics — rock bounces off ice, no forced combining.
3. **Attribute thresholds transform particles** — the ladders aren't special
   cases, they're what happens when a donated attribute crosses a line.

| Level | Who | Notes |
|---|---|---|
| 0 | PUSH | pure force — donates velocity to anything |
| 1 | LIGHT, DARK | energy; opposites → same level → ANNIHILATE (antimatter!) |
| 2 | DENSE, SPREAD, GLUE, REPEL | property carriers; opposite pairs cancel, non-opposites merge payloads (e.g. heavy glue) |
| 3 | SPARK, FROST | elemental matter; SPARK+FROST → steam puff (cancel) |
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
it re-pools. Bond strength IS the stickiness attribute, so everything
composes with no new rules:

- GLUE donated into a liquid → stronger bonds → slime/goo.
- REPEL/SPREAD donated → bonds shatter → mist/droplets.
- FREEZE → bonds lock rigid → the puddle becomes ONE solid (ice).
- MELT a solid → bonds loosen → liquid again (phase change = bond state).
- DIFFERENT materials never bond — chemistry decides instead
  (lava+water → stone+steam) or they simply coexist.

## The 12 emitters (per cast; re-fires re-emit; seal duration = emitter lifetime)

| Rune | Emits | On contact (world / higher level) |
|---|---|---|
| HeatUp | 3 SPARKS | +25° (3 hits on one thing = +75°) |
| HeatDown | 3 FROSTS | −25° — SYMMETRIC; freeze thresholds rescaled instead (below) |
| StateSolid | 3 medium SOLIDS of the drawn-on material (fallback stone) | real Matter |
| StateLiquid | 3 LIQUIDS (fallback water) | real Matter |
| LuminanceUp | 3 LIGHTS | illuminate; attach as portable light. NO passive damage — light's damage is EARNED via the plasma ladder *(veto?)* |
| LuminanceDown | 3 DARKS | attach blind-aura; LOWER luminance of what they hit (invisible flame!) |
| StickyUp | 3 GLUES | glue what they touch; two recently-glued things touching → joined |
| StickyDown | 3 REPELS | repel each other + shove things apart |
| DirectionAway | weak push ZONE (flight survives) + 3 PUSH particles | donate velocity along the arrow to anything, incl. other particles |
| DirectionToward | same, pulling | |
| DensityUp | 3 DENSE | donate density: heavier, bigger, falls; merge-flag 3s |
| DensityDown | 3 SPREAD | donate lightness: smaller, splits/multiplies, eventually FLOATS |

**Temperature symmetry (game-first, per Marko "it's just a game"):** emission
stays ±25 both ways; instead the FREEZE thresholds move so freezing something
takes the same number of frost hits as igniting it takes spark hits
(freeze point mirrored around ambient from the ignite point).

## Ladders = attribute thresholds (not recipes)

- **LIGHT absorbs density → LIGHTNING** (erratic, strikes the HIGHEST
  surface/creature nearby) **→ absorbs more → LASER** (straight beam, pierces
  everything, highest damage in game, hits players too). LOCKED.
- **SPARK absorbs density → FIREBALL** (heavy, explodes on impact); spark+spark
  same-level transmute → FLAME (bigger, ignites). *(veto?)*
- **FROST absorbs density → ICE SHARD → GLACIER chunk.** *(veto?)*
- **DARK absorbs density → SHADOW** (creeping blind-cloud drifting toward
  nearest creature) **→ deep darkness + more density → VOID RIFT**: inhales
  nearby objects/particles for ~4s, then spits out a **DEMON** (Marko's
  design, `Creatures/Demon.cs`): black, rampaging, hostile to everyone,
  SIZED by the rune that tore the rift — and it ABSORBS whatever element
  touches it, BECOMING the last thing it ate until it expires (fire → flame
  demon, frost → ice demon, eats lava blobs whole → lava demon…). It leaks
  an aura of its current element as real particles, so players can RE-SPEC
  a rampaging demon by shooting elements at it. Dark = antimatter = the
  summoning school.
- Matter ladders already live: water⇄ice⇄steam, stone⇄lava, wood→coal→diamond.

## Fear & perception layer (Marko's rule: zombies fear what they can SEE)

- Dangerous particles (extreme temp, lightning, laser, rifts) emit a FEAR
  signal — but only to creatures that can SEE them: vision cone + the
  particle's EFFECTIVE LUMINANCE above a visibility floor.
- **Invisible flame:** DARK donated into flame lowers its luminance below the
  floor → zombies can't see it → walk straight into it. Darkness = the
  stealth layer of the magic system.
- **LASER = universal terror:** every creature that sees it flees, bosses
  included (it one-shots most things).
- Blinded creatures fear nothing (can't see danger either — double-edged).
- Googly eyes react: Scared pinpricks at visible danger, Wowed dilation at
  big spectacles (existing moods, new triggers).

## Particles × world

- Matter blobs: full existing chemistry (heat melts, frost freezes, dense
  grows/solidifies, spread fragments/lightens).
- Creatures: sparks heat (ignite at threshold), frosts chill (freeze),
  glue roots, repel shoves, push launches, dense = fat slow zombie with more
  HP *(veto?)*, spread = small fast frail zombie → eventually floats.
- Players: same rules, friendly fire stays ON.

## Safety rails

- Global particle cap ~120 (oldest dies first), matter cap 90 stays.
- SPREAD duplication halves strength per generation; floor kills it.
- Particle life ~4s baseline; emitters re-emit every few seconds while the
  seal lasts (circle = 36s of periodic mayhem).
- One VOID RIFT at a time; rift cannot swallow players (drag them only).

## Implementation notes (for the build session)

- One `SpellParticle` component: matterLevel + attribute struct
  (temp/lum/density/stick/velocity) + one collision resolver implementing
  THE LAW; ladders = threshold checks after every absorption.
- Reuses: Matter (levels 4-5 chemistry), EffectMote retires into
  SpellParticle, Thermal, Creature statuses, ZombieBrain fear via WorldEvents,
  blind system for visibility gating, MatterFX colors.
- Zones stop applying field effects (except weak Direction force); emission
  replaces fields. Rings stay as area markers.
- Liquid cohesion = attraction force + blob-merge within a small radius,
  NOT physical joints (perf); the force reads the stickiness attribute.

## Open vetoes for Marko

1. Fire/ice ladders (fireball, ice shard/glacier) — keep?
2. VOID RIFT as written (shadow creature hostile to all)?
3. Plain light = illumination only (damage earned via ladder)?
4. Dense-fed zombie = fat tanky zombie?
5. Level-2 non-opposite merges (heavy glue etc.) — keep generic merge?
