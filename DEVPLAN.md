# Spelly Zombie — Functional Completion Plan (no art)

Goal: a **functionally complete, co-op playable game** as fast as possible.
Art (models, textures, animations, capsule/trailer) explicitly excluded — the
graybox + bean-zombies + googly eyes ARE the placeholder art and they read
fine. Placeholder CC0 **audio** is included here because silence breaks
playtests, and juice is a P0 spec item.

Governing spec: MC-sized scope, Steam page → Next Fest demo → wishlist gate.
Out of scope stays out: no story, no PvP, no procgen, ≤12 symbols, no
Noita-depth sim.

---

## Already functionally DONE (don't rebuild, only tune)

- Drawing: freehand ink on any surface, body ink rides characters, erase/repair, ink budget
- Recognition: $P matcher, 12 glyphs, per-owner grimoire, in-game template re-recording
- Seals: self-close / multi-stroke / lasso loops, integrity & breaking, duration by edges, last-drawer ownership
- Spells: physics rune zones, pressure bombs, matter conjuring + chemistry (freeze/melt/burn/transmute/fragment), 14 named ComboBook combos with banners + kickers, zones ride their ink, **player flight** (feet seals), force/knockback correctness
- Creatures: status system (burn/freeze/stuck/slip/knockdown), googly-eye perception, 3-slot brains (gossip, grudges, patrol, trance), 3 zombie kinds incl. drawing wizards, rune-card drops
- Meta seeds: per-owner grimoire, starting-rune choice, treasure/wallet
- Demo scaffolding: wave director (Z), HUD, scene/map builder tools

## Definition of "functionally complete"

4 players over real internet play 15–25 min rounds vs escalating waves,
earn/spend ink, down/revive each other, wipe or win, see the seal gallery,
and every P0 acceptance test in the spec passes without a developer present.

---

## Phase A — Complete the offline game loop (~week 1)
*Everything here also defines what netcode must replicate — do it first.*

**A1. Round structure** (new `Game/RoundDirector.cs`, replaces demo ZombieDirector waves)
- Round N: budgeted zombie spawns (count/mix/speed scale with N), entry-point spawning, round intermission (20s draw/prepare phase), round banner.
- Lose: all players down → run summary (rounds, kills, combos found, riches). Demo cap: round 10.
- Acceptance (spec): solo player reaches round 10 with an intact difficulty curve; session 15–25 min.

**A2. Ink economy**
- Ink meter per player: strokes consume ink by drawn length; kills drop ink pickups (or auto-award); intermission trickle.
- Draw-fast-vs-draw-well tension is THE tuning target. HUD ink bar.
- Zombie scribblers exempt (their ink is flavor).

**A3. Player down/revive (kills demo mercy in matches)**
- 0 HP → downed (crawl, can't draw), teammate revives by holding E 3s (co-op moment), solo = 15s bleed-out. All down → wipe.
- Friendly fire stays ON (spec).

**A4. Zombie roster 3 → 5 (spec types, all bean variants)**
- Walker ✓ · Brute = charger tuned ✓ · Spitter = scribbler (already casts at range) ✓ rename/tune
- **Runner**: fast, frail, cowardly gossip profile. **Swarm**: 0.5× scale packs of 5–8, 1-slot brains.
- Wire spawn mix into RoundDirector escalation.

## Phase B — Steam online co-op 2–4 (weeks 1–3, in parallel, highest risk)
Stack: **FishNet + FishySteamworks transport** (free, maintained) + Facepunch.Steamworks lobbies. Test on Steam app 480 until our appid exists (needs the $100 Steamworks registration — Marko).

- **B1** Lobby: host/join via Steam friends + invite overlay; 4-slot lobby scene.
- **B2** Player sync: transforms, look, health, ink meter. Host-authoritative.
- **B3** Stroke replication: reliable batched node streams (strokes are already surface-anchored data → send surface id + local points). Remote strokes join local seal detection deterministically on host; host broadcasts seal events (id, owner, runes, outcome).
- **B4** World authority: host runs zombies/brains/matter/spells; clients render replicated state (zombie pos/state/mumbles, matter spawn/phase events, combo banners). Matter cap already exists.
- **B5** Ownership online: OwnerId → Steam id mapping; per-player grimoire/wallet.
- **B6** Join-in-progress + disconnect handling (drop to lobby, host migration = out of scope, host quits = run ends).
- Acceptance (spec): 4 players over real internet complete 10 rounds; a misfire kills a teammate and everyone sees the seal that caused it.

## Phase C — Feel: juice + audio placeholders (week 3–4)
- **C1** Screen shake (casts, explosions, charger hits), hit-stop/slow-mo (0.3s at 0.3× on multi-kills and team-kill misfires), cast flash already ✓.
- **C2** CC0 SFX set: draw scritch, seal close hum, per-combo casts, zombie mumble grunts (pitch-shifted per line), ignite/freeze/shatter, downed/revive stingers. One AudioManager, no art dependency.
- **C3** Misfire legibility: fizzle poof + sad trombone-style cue; combo banner already ✓.
- Acceptance (spec): 10-min session produces 2–3 organic "what just happened" moments readable to a viewer.

## Phase D — Streamability & demo wrapper (weeks 4–5)
- **D1** End-of-round **seal gallery**: every seal drawn that round rendered as its ink shape + owner + quality score + what it did ("look at this idiot's circle" moment). Data already exists (strokes/scores).
- **D2** **Seal autopsy replay**: on team-wipe or spectacular kill, 5s overlay replaying the seal being drawn (node timestamps already ordered) → outcome text. One-click PNG save of the seal; video capture stays out (Steam/OBS do it).
- **D3** Flow: main menu (Host/Join/Solo/Options/Quit), pause, options (sens ✓, volume, invert), end screen with wishlist CTA link.
- **D4** Input breadth: controller radial draw-assist (right stick = cursor, assist snaps ring closure), Steam Deck pass, tablet/pen verification.
- **D5** Demo caps: round 10, 4 of 6 rune families enabled, co-op ON, CTA persistent.

## Phase E — Hardening & playtest loop (week 5–6)
- **E1** Perf pass: replace per-tick `FindObjectsByType` (zombie retarget, pickups, chicken) with static registries; pool flame motes/matter; Detect() budget check at 100+ strokes.
- **E2** Instrumentation: local CSV per run (rounds reached, casts, combos discovered, misfire rate, deaths by cause) — this is the tuning + GO/NO-GO evidence.
- **E3** Playtest cadence: 2 sessions/week with 3–4 externals from week 3; fix-list triage after each.
- **E4** Freeze flags: PoseStudio & pose-seals behind a const bool for demo builds (frozen scope, keep code).

---

## Marko's non-code checklist (parallel, blocking items marked ⚠)
- ⚠ Steamworks registration ($100) + appid — blocks B1 real lobbies & the page
- ⚠ Steam page copy + capsule (art exception — it gates everything per spec)
- Recruit 3–4 recurring playtesters (co-op comedy needs friends, not devs)
- CC0 SFX shortlist approval (I pick candidates, you veto)
- GO/NO-GO calendar: page live → Next Fest submission window

## Standing risks (watched, not feared)
1. **Netcode schedule risk** — mitigated by starting week 1 and by host-authoritative simplicity; if FishNet integration stalls >1 week, fallback is Steam P2P with manual state sync for players+seals only and host-local zombies (degraded but demoable).
2. **Recognition tuning on real players** — E3 playtests feed template/threshold tuning; the re-recording tool already exists in-game.
3. **Scope creep** — anything not in this file goes to a POST-GATE.md parking lot.
