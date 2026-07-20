# ART TODO — placeholders on the Workshop map (and around it)

## ⭐ THE ONE RULE — `Assets/_Game/Resources/Custom/`

**A prefab in that folder, named for its hook, replaces what the code builds. Drag → rename →
edit. No buttons, no wizard, no lists.** Missing = placeholder appears, so nothing ever blocks.

Every prefab landing there is AUTO-HEALED on save: runtime materials become real `.mat` assets
in `Custom/Materials`, broken scripts and stray ink get stripped, dead material slots get a
console warning naming the object. **Drag during PLAY MODE** — that's when runtime materials
are still alive to capture. (Drag after stopping = pink, and the console tells you so.)

### The registry — exact name → what it replaces → what to make it FROM

| Name (exact) | Replaces | Make it from |
|---|---|---|
| `PlayerBody` | the player's entire worn body — your materials, eyes, mouth, worn pieces all kept verbatim | **Play → drag `SZ_Player/Body` from Hierarchy** into Custom/ (or menu *Bake PLAYER Body*) |
| `ZombieBody` | every zombie's body (no code tint, no code mouth) | **Play → press Z → drag `Zombie_*_Dress/Body`** (or menu *Bake ZOMBIE Body*) |
| `Wand` | the right-hand pen | your FBX → prefab. **Pivot at the grip, +Z out of the fist.** Keep the root unscaled (the ink vial mounts on it) |
| `Grimoire` | the left-palm book | your FBX → prefab. Pivot at the spine, authored closed. Optional child `PageAnchor` = where page art spawns; optional Animator with `Open` bool / `Flip` trigger |
| `Eyes` | the googly-eye rig on every head | your FBX → prefab. Children named `Eye`, each with a child `Pupil`, get full googly behavior |
| `Chest` | the mystery chest | your FBX → prefab. Optional child `Lid` (pivot on the hinge edge, identity = closed) swings open |
| `SpellPage` | the dropped rune-page pickup (whole object) | your FBX → prefab. Spin/bob/collect stay code-side |
| `SealTablet` | the slide-weapon look | your FBX → prefab. Child named `Slide` becomes the racking part |
| `RuneChamber` | the rune-chamber look | your FBX → prefab. Children `Strip`, `Bridge`, `Frame` become the mechanism |

**Textures** (same folder, same rule — no prefab needed):

| Name | Replaces |
|---|---|
| `Cursor` | the quill cursor (import: Read/Write ON, compression None) |
| `SpellPage` | the dropped page's sheet art |
| `GrimoirePage_Heat` / `_State` / `_Luminance` / `_Sticky` / `_Direction` / `_Density` | that family's page art, glyphs stamped on top |
| `GrimoirePage_<Family>_Full` | that family's COMPLETE spread — code draws nothing over it |
| `GrimoirePage_Lesson` / `GrimoirePage_Lesson_Full` | page one (the seal lesson) |
| `Loc_<code>` (TextAsset) | a language file |
| `Music_Chill` / `Music_Action` (audio, WAV) | the soundtrack pair — both loop in lockstep; the game crossfades to Action while a wave runs, back to Chill for prep/lobby/menus |

### Not in the shelf (and why)
- **Buildings, cauldrons, fences, the workshop, terrain** — scene objects. Builders scaffold them
  ONCE and never regenerate; edit them in the scene freely. To reuse one elsewhere: select it →
  *Spelly Zombie → Make Prefab From Selection* (saves its materials too).
- **Costume pieces** — `Prefabs/Costume/` named `Hat_x` / `Cape_x` / `ZHat_x` / `ZChest_x`…, then
  re-run *Build Character Rig* once to register. (They're a random-pool, not a single override.)
- **THE WIGGLE CONTRACT (the scarf!)** — on ANY costume piece (player or zombie), name a child
  `Wiggle` and that part comes alive: lags behind movement, swings on turns, droops, settles.
  For a flowing tail, nest a chain: `Wiggle_1` → `Wiggle_2` → `Wiggle_3` (pivot at each joint,
  tail extending from parent pivot toward child pivot). Everything NOT named Wiggle stays rigid
  exactly where you modeled it. The scarf itself is just a Cape-slot catalog piece: rigid collar
  ring + a Wiggle chain for the tail. Cloth simulation is retired — this replaces it.
- **Animations** — drop clips in `Art/Zombie/`, re-run *Build Character Rig*. Filename decides the
  slot (walking/running/attack/reaction hit/stand up/scratch/agonizing…).

**Materials on demand:** select anything (scene or play mode) → *Spelly Zombie → Extract
Materials From Selection* → every runtime material on it becomes an editable `.mat` asset in
`Custom/Materials`, and the object keeps its look. Drag those onto your prefabs freely.

**Anything code-spawned that lacks a hook: say the name in chat, it gets one that session.**

Everything below currently exists as graybox/kit stand-ins with a **code hook already wired** —
make the model, drop it at the hook, zero code changes. Blender → FBX, meters, Y-up default
export; character-worn pieces at identity in character space; hand-held pieces with the **grip
pivot** convention (pivot where the hand holds, +Z pointing away from the palm).

## P1 — in every clip, make these first

| Thing | Today's stand-in | Hook (drop it here) | Notes |
|---|---|---|---|
| **Wand** | primitive cylinder shaft | `Resources/Custom/Wand` prefab (PrefabVault) | First-person, on screen 100% of play. Grip pivot. The `WandInk` vial child attaches by code — keep the root unscaled. |
| **Grimoire** | two cubes (cover+pages) | `Resources/Custom/Grimoire` prefab | Held in the left palm, pages face up. Grip pivot, ~17×23cm closed. |
| **Grand Cauldron** | Fantasy-kit cauldron ×1.25 | replace the `GrandCauldron` object in the scene (or prefab it and tell me — I'll make the builder prefer yours) | THE map identity: heart, ink well, lose condition. Wants rune-carved rim, ink glow. Siege code will later want 2–3 damage looks (can be material swaps — your call). |
| **Cape (remake)** | flat board mesh, cloth can't drape it | replace `WizardCape.fbx` | The cloth checklist: **pivot at the top attach edge, modeled hanging down, subdivided ~12×8+, Read/Write ON**. Team tint + rune stamp apply by code. |
| **Zombie body — YOURS entirely** | shared bean, code-tinted | **`Resources/Custom/ZombieBody` prefab** — your mesh + your materials replace everything; code adds NO tint, NO mouth. Rig on the same Mixamo skeleton (or copy SZ_Body's avatar in import settings) and all zombie animations play on it. | The total-control lever. **How to make it: Project panel → the FBX asset → right-click → Create → Prefab Variant → drop in Resources/Custom → edit its materials on the variant.** Never prefab a runtime instance from the Hierarchy. |
| **Player body — same lever** | wired SZ_Body, code skin tint | **`Resources/Custom/PlayerBody` prefab** — identical recipe; your materials survive untouched. | |
| **Zombie jaw/mouth + rags + hats** | my dark sphere "mouth" + naked bodies | `Prefabs/Costume/` prefabs named `ZHat_x`, `ZChest_x`, `ZHead_x`… (wizard rescans on Build Character Rig) | Socket pieces at identity. The sphere mouth dies the day a real jaw lands (`ZombieFlavor.GiveMouths=false`). |
| **Player hats/capes** | placeholder team hat + cloak | `Prefabs/Costume/` named `Hat_x`, `Cape_x`… | Same pipeline; catalogs (SZ_WardrobePlayer) list them for the lobby picker later. |

## P2 — the siege core will need these (build with/after it)

| Thing | Today's stand-in | Hook | Notes |
|---|---|---|---|
| **Warded door** | kit door stretched ×1.28; observatory ward is a **purple cube** (sorry) | one model reused for all 7 wards — tell me the prefab name and the builder uses it | Reads as "sealed by magic": carved arch slab, ward glow. ~1.4×2.3m. |
| **Window boards** | nothing yet | a `Boards` prefab with 2–3 damage states (whole / chewed / one plank) | The Solid-rune repair verb fills window frames with these. Kit planks can prototype. |
| **Lore shrine** | empty marker `LoreShrine_Study` | shrine/mural model at that spot | The Spark of Tales link point. An illustrated mural slab beats text (localization rule). |
| **Rune page pickup** | (economy lands with siege) | floating torn-page model | Kit `Scroll_1/Paper` fine to start. |
| **Mystery chest** | code-animated kit chest | hero chest model (+ optionally a BEAR reveal variant — the gag deserves art) | |

## P3 — whenever the mood strikes

- **Grimoire/spell-page art**: `Resources/Custom/GrimoirePage_<Family>` (art under the glyphs), `_Full` variant (your complete spread), `GrimoirePage_Lesson`, and `Custom/SpellPage` (the generic dropped-page sheet). Dropped pages automatically match book pages.
- **Weapon skins**: slide tablet + rune chamber are graybox mechanisms; `Wardrobe.WeaponSkin` hooks exist.
- **Cursor texture**: `Resources/Custom/Cursor` (Read/Write ON, compression None) replaces the procedural quill.
- **Demon body**: the void-rift demon still has no rigged body; Demon wardrobe catalog already exists.
- **SZ_Body jump animation**: one Mixamo clip, whenever.
- **Perk pot variants**: currently the same kit cauldron ×0.85 — per-brew colors could just be materials.

## Explicitly NOT yours to make (covered elsewhere)
- Workshop building/rooms — kit-built by the WorkshopBuilder, restyle freely in-scene.
- Terrain, graveyard POIs, forest — your terrain pass + Kenney Graveyard Kit (CC0).
- Zombie/player body + animations — done and wired.
- Ship & carriage — later maps; hero versions get their own session when their turn comes.
