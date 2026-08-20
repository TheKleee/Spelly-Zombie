# Spelly Zombie — AI Handoff Brief
_State as of 18 Aug 2026. Read this before touching anything._

## 1. The game

Unity 6.3 LTS / URP / FishNet + FishySteamworks. Solo dev (Marko, Serbia, 7 active years
of gamedev). 1–12 player multiplayer where **you draw runes to cast spells**.

**One mode only: Wizards vs Acolytes.** Prop hunt × Counter-Strike, the cauldron is the bomb.
- **Wizards** hunt, and scale by **collecting**: absorb world objects → unlock runes → draw
  glyphs inside a seal → cast.
- **Acolytes** hide as scanned objects, summon zombies, corrupt the pot, and scale by
  **mischief** (deeds unlock spells — see §5).
- Dying is a *mode*, not a menu: you become a ghost and possess your team's spells
  (wizard ghosts) or zombies (acolyte ghosts).
- Win = 5 endings in `GAME_MODE_ACOLYTES.md` §WINNING (pot empty → acolytes · wizards dead →
  acolytes · timer + green pot → acolytes · timer + clean pot → wizards · acolytes dead +
  clean pot → wizards).

Target: steal Meccha Chameleon's churned audience. $5.99, Japanese-first, ~100k copies month one.
Release strategy is **traction-first**; Steam Next Fest is a fallback, not the plan.

## 2. How to work with Marko — read this twice

These rules were learned the hard way. Breaking them costs him removal work and trust.

1. **Build only what he explicitly asks for.** Suggestions are welcome as prose; unaccepted =
   refused. Silence = no.
2. **Do not implement fixes you believe are right without consulting him.** Report a problem,
   describe the intended fix in 1–2 lines, wait for his yes. Mechanical corrections of your own
   broken code are fine; anything containing a *design opinion* is a proposal first.
3. **He must be able to change anything.** No hardcoded content, no prescribed authoring
   structure. Code *adopts* what his prefabs declare (any collider, any bone count, any scale) —
   it never dictates. If it's content, it's data or a prefab or an Inspector slot.
4. **Give him the physical authoring surface, not just knobs.** He caught this twice in one day
   (auto-only roundabouts, auto-only paths). Anything that shapes the world should be
   placeable/draggable in the scene, with generation as fallback.
5. **Never auto-detect on his content.** Expose a slot, use what he set, fail loudly when empty.
   Relations are declared (hierarchy parenting, Inspector references), never guessed by position.
6. **Reuse, don't duplicate.** Grep for the system that already does it and extend it. Two copies
   of one rule is the bug he hates most.
7. **No new GameObjects/primitives at runtime for anything he could author.** UI plumbing is the
   exception (UIKit builds UI in code and adopts his prefabs when present).
8. **No `Resources/` for new assets.** Existing hooks there stay; don't add.
9. **Never touch git.** No add/commit/push/branch. Ask him to commit and wait.
10. **Comments: few, short, factual.** No emojis, no quoting his rulings, no essays.
11. **Writing style to him: short bullets.** He will not read walls of text.
12. **His word > code > docs.** `GAME_DESIGN.md` in the repo is STALE — never quote it as canon.
    When his spec contradicts existing behavior, the behavior is the bug.

## 3. Where we are right now

The demo needs a playable map for marketing video + screenshots. Priority chain he set:
**map → multiplayer testing → spells.**

**Just finished (Aug 17–18): the Spelly Island map generator.** Working end to end:
terrain synthesis, coastlines, paths, biome fill, water, fog, world wrap.

**In flight, his side:** prefab variants for the fill lists (houses from Medieval Village MegaKit
pieces — there are no ready house prefabs in the project), `ObjectBox` sizing, `PathPoint` doors,
TerrainLayer brushes, the blob prefab, the Center landmark, the host crown (use `Zekirak/Crown`).

**In flight, my side:** the acolyte mischief system (§5) — just un-parked into the demo.

## 4. The map generator (the big active system)

`Assets/_Game/Scripts/Map/` — everything below is authored in the scene
`Assets/_Game/Scenes/Maps/Spelly Island.unity`, with `SpellyMap` as the orchestrator
(big **GENERATE PREVIEW** button in its Inspector; `AutoRegenerate` rebuilds ~1s after edits;
purple path lines draw live in the Scene view).

**The model: boxes define the world.**
- `Biome` (abstract) → `GroundBiome` / `LiquidBiome`. **No kind enum** — a biome is its name +
  its definition, so a modder can make "Magma" or "Space" with the same components.
- A box **is** its elevation band (its own bottom/top Y). Terrain size = the union of all boxes.
- **Layers cut**: higher `Layer` carves through lower where they overlap (beach slab + forest on
  top = beach survives as a ring). New biomes auto-take the next layer.
- **`ProtectedCore` (0–1)** = a circular inner area no higher layer may cut and no shuffle may
  cover. Center biome = 1 (wizard spawn + where ink grounds when all pots die). Default 0
  (fully erasable, by design).
- `BiomeContainer` fences listed biomes inside it, so land never reaches the wrap seam.
- **Randomized maps** reshuffle boxes on X,Z per seed; Y bands and sizes never change; boxes move
  only within their lower-layer host; protected cores are never covered.
- **Terrain**: per-point winner box → band + `FloorNoise`; `Softness` smooths seams; plateau tops
  are restored after smoothing; per-face zero-mean border waves (`CoastWobble`/`WobbleScale`)
  make organic coastlines and are clamped to 25% of a box so small lakes wobble instead of drift.
- **Painting**: each biome's `FloorLayer` + `PathLayer` TerrainLayers; cliffs auto-paint by slope
  with a gradient; soft round brush (5 taps per texel).
- **Liquids**: `LiquidBiome` has Tint / Surface prefab / Buoyancy. `LiquidSense` on the camera
  fogs the world in the liquid's color underwater and hides the skybox;
  `SimpleFPSController` swims (slow sink, Space = stroke, 0.6× speed, no drowning).
- **Wrap**: `LoopWrap` mirrors players at ±`LoopDistance`; horizon fog hides the seam; the water
  shader (`SZLiquidSurface`) receives fog and goes opaque with depth so the world edge never shows.

**Paths — his authoring first, generation second.**
- `PathNode` = HIS placed marker. `LinksTo` draws exact roads; `Radius > 0` = roundabout (ring
  path + keep-clear plaza); `JoinWeb` opts out of the auto web.
- **Parent a node under a Biome** → it belongs to that biome (rides its shuffle, owns its layout).
  A biome containing any authored node gets **zero** auto nodes. Unparented = world-fixed junction.
- Fallback web for unauthored biomes: multiple waypoints per biome, spanning tree + nearest-neighbor
  links (junctions and loops, not spokes).
- Routes are found by **slope-cost A\*** (natural switchbacks, water-averse), bent by the local
  biome's `PathCurve` (0 = street, 1 = trail), then carved with **banked shoulders** that widen
  with cut depth — no trench walls.
- **Nothing spawns on a path**: the whole object claim is tested against the ribbon mask.

**Fill — two lists per biome, his law.**
- `Props` = abundance (fills the place). `Sources` = scarcity (absorbables), guaranteed
  `MinSources`–`MaxSources` (1–3), **never coupled to prop counts** ("I can't spawn a bunch of
  houses with torches or wizards get fire instantly").
- **Hidden riders**: a source authored *disabled* inside a prop (a torch on a house wall) gets
  **revealed** by the guarantee pass. One quota covers riders + standalones.
- `ObjectBox` on a prefab = its claim (spacing + path avoidance). `PathPoint` children = doors:
  the placer **faces the entrance at the nearest trail** (author doors on **+Z**) and grows a
  carved spur to it. No trail in reach → face the biome center.
- Placement law: one ray from box top down to box bottom, terrain hits only, slope-checked,
  path-free, claim-free — else the field is skipped. *Fewer objects beat janky objects.*
- Per-biome `Cauldron` slot + map-level `CauldronLimit` (3): candidates roll per seed, winners get
  one pot each. Center biome hosts nothing — only the ink at the endgame.
- `Landmark` slot per biome = his centerpiece prefab at the biome's center.

**Not built yet:** spawn points (wizards center, acolytes scattered far), pot/ink game-layer
binding, chunk merging, town fences with entry gaps. **Bridges are parked by his ruling.**

## 5. The acolyte mischief system (next feature, in scope for the demo)

His design, ruled into the demo on 18 Aug because it fixes acolyte scaling.
Full spec in his memory file; summary:

- Wizards scale by collecting, **acolytes scale by deeds**. 6 spells, ~1 min each, cast with
  **existing rune glyphs** (no new glyphs — only page icons + VFX). **No acolyte spell affects
  acolytes or themselves.**
- 3×2 unlock symmetry — each activity has a success *and* a fail/alternate trigger:
  hiding (fooled a wizard / survived his cast), fighting (you killed him / he killed you),
  objective (corrupted the pot / failed to).
- The six: **Decoy** (objects/players flee wizards) · **Reveal** (blown objects burst poison) ·
  **Death needle** (a wizard you already killed dies to ghost for 1 min) · **Grimoire-switch
  curse** (the wizard keeps his team but receives the *acolyte book* — a disarm, delayed ~30s,
  unannounced to the victim, red-eye tell to everyone else; his own replacement for a
  team-switching version he rejected) · **Evaporation ink** (evaporates wand/pot/map ink) ·
  **Transformation ink** (target becomes your last scan for 3s).
- **Rune carryover**: a cursed wizard's already-unlocked runes appear as their corrupted acolyte
  pages, so the curse *translates* his arsenal instead of stripping it.
- **Zombie summon unlocks** (cheap, could ship first): scan something → melee zombie;
  transform back → ranged zombie. Onboarding disguised as progression. Note this **changes** the
  built `RuneLibrary.AcolyteKit` gate, where acolytes currently start with all 4 runes.
- **BUILT already: the unlock tell** — `UI/RuneToast.cs`. His grimoire page art on a paper card
  (his page PNGs are transparent), right side, newest at the anchor, older pages climb and dim.
  One door for both sides: `RuneToast.Show(rune)` / `Show(texture)`. Hooked in
  `Grimoire.UnlockRune`, fires only on genuinely new runes.

## 6. Known gaps and traps

- **⛔ Multiplayer sync is the biggest hole.** Remote players' *sides* don't replicate yet, so
  shapes/disguises, the acolyte exit-poison cloud, the zombie-death whistle, the death burst,
  hat colors and green pot growth are all local-only. They are riders on **one sides-sync schema
  pass**, which is the first job of the MP phase. Do not build them piecemeal.
- **⛔ Proximity voice is unbuilt** and is the biggest missing product piece (every viral clip in
  this genre is voice-first). No text chat, ever — that's a hard ruling.
- **⛔ Zombie bug history**: read the zombie handoff memory before touching zombies.
- **Dead ideas that must never come back**: team switching, conversion, souls, zombie waves,
  the chariot, escort/co-op mode, bleed-out, weapons, the finder mechanic for the corrupt pot.
- Seeds come from a **daily pool** (hash of UTC date + a roll), not a host field — hosts don't
  know what a seed is.

## 7. Open questions he hasn't answered

1. **Absorb ecology**: where do the runes **Slick**, **Compress/Dense** and **Spread/Thin** live?
   (Rune = object × biome: forest pebble → Dark, beach rock → Light, peak → Chill, town → Solid +
   Fire, water → Liquid + Sticky.)
2. **Are the old ordered triples dead** — one source grants exactly one biome-flavored rune?
3. Do acolyte deed-unlocks **persist through death**?
4. Green scan-tint trail: canon, but **not verified in code**.

## 8. File map (the parts that matter)

```
Assets/_Game/Scripts/
  Map/        SpellyMap · Biome/GroundBiome/LiquidBiome · BiomeContainer ·
              PathNode · PathPoint · ObjectBox · LiquidSense · LoopWrap
  Game/       RoundDirector (referee) · CauldronEconomy · MatchLobby · LoadEgg ·
              HostCrown · LobbyRespawn · Breakable · SkyBeam
  Player/     SimpleFPSController · ShapeShift (acolyte disguise) · GhostState ·
              GrimoirePages · Analyzable (absorb) · XRayGlow
  Recognition/ Grimoire · RuneLibrary · RuneGlyph (the $P recognizer)
  Spells/     Spell · SpellParticle · RuneGrammar · Damageable · GrammarAreas
  Net/        NetSync · NetGame · SteamLobby · MatchLobby wiring
  UI/         UIKit · RuneToast · CauldronHUD · LoadingHints
Assets/_Game/Shaders/  SZLiquidSurface · SZEggDissolve · SZXRayCircle
Repo root:     GAME_MODE_ACOLYTES.md (canon) · GAME_DESIGN.md (STALE)
```

Tuning lives in `DrawingConfig.cs` (every value is overridable from
`{persistentDataPath}/sz_tuning.json`). Add new numbers there, never as literals.
