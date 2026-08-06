# SPELLY ZOMBIE - MODE 2: WIZARDS vs ACOLYTES (ruled 2026-08-06)

Marko's own words for it: **spell drawing prop hunt plus Counter-Strike, with the
cauldron instead of the bomb.**

This is a SEPARATE MODE from the co-op raid in GAME_DESIGN.md. Where the two
disagree, neither is wrong. They are different games sharing an engine.

Supersedes the earlier chameleon version of this mode entirely (body-paint
camouflage, Meccha Chameleon collaboration, possession of zombies, transformation
expiry). None of that is in the design any more. Do not reintroduce it.

---

## One sentence

Wizards defend a cauldron and hunt hidden acolytes; acolytes hide as furniture,
summon the dead, and corrupt the pot; and killing anyone slowly turns you into
what you killed.

## Why it passes the design filter

Familiar frame plus exactly ONE novel thing. Frame = prop hunt. Novelty =
drawing spells. **Both sides draw**, so nothing competes for the novelty slot.
The chameleon version failed this filter because camouflage painting was a second
novel verb; transformation is not, because it is the grimoire's existing
detect-and-absorb verb pointed at a new target.

---

## THE ACOLYTE

An enemy wizard with a strange grimoire (the Witch Hat Atelier hats idea) and a
**green, corrupted wand**. Green already means corrupted in the co-op mode, so the
visual language is free.

**Can do everything a wizard can physically do:** walk, grab, carry, lift objects
with ink levitation.

**Cannot:**
- absorb runes or cast real spells
- draw on their own body

**Instead of learning runes, they scan objects to learn FORMS.** Aim the grimoire
at a chair, absorb it, and you can become that chair. Same verb, same key, same
`Analyzable` component as the wizard's rune absorbing.

**Transformed:** you can move, you CANNOT draw, and you see yourself in third
person. Transformation does NOT expire. Learned forms are permanent for the round.

**Weaker than a wizard one-on-one.** This is what makes contesting anything
dangerous for them.

### Acolyte ink economy

- their wand decays like everyone's
- **the ONLY refill is scanning objects**, which is also how they gain forms
- scanning is **per instance**: that specific chair is used up
- so the map is a finite ink pool, and running it dry is the acolyte's clock

Consequences that fall out of this and are wanted:
- to refill they must leave cover, walk to something unscanned, and stand there
- spent props are a **trail** a wizard can read
- an acolyte who hides perfectly summons nothing and therefore wins nothing

### Ink ore

Limited number on the map. Acolytes **cannot corrupt them and cannot transform
into them**, but they **can lift them**, so their only use for ore is DENIAL.
Contesting ore means going where wizards are while being weaker in a fight.

**Guardrail: ore must never be permanently destroyable.** If it can be dumped
somewhere unrecoverable, wizards can be starved into an unwinnable round.

---

## ZOMBIES

**Zombies are SPELLS.** They belong to their summoner and they expire on a
duration. That replaces any per-player cap: a busy acolyte has zombies, an idle
one does not.

Summoned by drawing zombie icons plus a direction arrow **inside one seal**. The
arrow commands where they go, so a single drawing is the whole order. Uses the
existing arrow glyph; corrupt ink is what makes an arrow command the dead instead
of pushing matter. **No new glyphs.**

**Two types, from the rune pair's two directions:**
- **melee** - corruption cloud around them (Warcraft 3 undead disease cloud), better
  corruption up close
- **ranged** - lob corruption balls at the cauldron (Warcraft 3 catapult with the
  disease cloud upgrade), weaker and slower corruption, **deliberately inaccurate**

**Behaviour:**
- weak and easily destroyed
- **slower than players**, so a wizard can always disengage
- very afraid: they flee whoever chases them, and **stop fleeing when nobody is
  chasing them**
- **they attack players who turn their back on them.** This protects their master
  and stops wizards from simply running past
- they poison and can kill players
- **they CANNOT corrupt wands.** This is load-bearing, see the anti-throwing rule
- **their role is to be chased**, to drag wizards away from the pot

**Kill credit follows spell ownership:** if a zombie kills you, your soul binds to
the acolyte who summoned it.

Tuning relationship to protect: thrown balls are **chip** corruption that wizards
can out-wait; an acolyte corrupting the pot in person is **burst**. Otherwise the
sneak-to-the-pot play dies.

---

## THE CAULDRON

Sits at the centre of the map. Wizards spawn there. The grimoire compass points
at it.

**It has no HP. It gets CORRUPTED**, which is a state, not damage. Consistent with
the Ink Ore, which also has no bar.

**Either side can carry it** with ink levitation. It starts near the wizards and
can be stolen. Nobody can hide while hauling a cauldron, so stealing is the
acolyte's most exposed act and their biggest play at once.

**Two ways for wizards to clean it:**
1. **Wait it out.** Corrupted ink evaporates on its own. Slow, free, and you keep
   the full value of your ore.
2. **Feed ink ore until it turns black.** Fast, but the ore is spent for nothing,
   and ore is finite.

Both are existing systems: ink evaporation and `FeedOre`/`Blacken`.

The tradeoff sharpens over the round. Early, burning ore is cheap. Late, ore is
scarce and acolytes have been denying it, so the fast option may not exist.
**Banking ore for the endgame is correct play.**

**The pot is the scoreboard.** Green means acolytes ahead, black means wizards
ahead, readable from across the map with no interface.

---

## CORRUPTION AND CONVERSION

**Corruption comes ONLY from souls. Souls come ONLY from kills.**

Kill someone and their soul binds to you. It helps you, and it corrupts your wand
the whole time. The help is the bait.

**The wand is the progress bar.** It corrupts from the TIP down, which is the part
writing consumes first, so:
- **drawing burns corruption off**, because you spend the green end
- **clean refills dilute it**
- when green reaches the grip, **you convert**

Low ink plus heavy corruption means your whole remaining wand is green. You are
nearly gone and everyone can see it on your hand. No UI, ever.

**Converting = you fully become the other side.** New team, new grimoire, mostly
empty, exactly as if you had started the round on that team. You lose your runes
or your forms.

That makes conversion a rubber band aimed at whoever is playing best, since kills
are the only source and conversion resets you to zero.

### THE ANTI-THROWING RULE (do not break this)

**Zombies cannot corrupt wands. Only souls can, and souls only come from kills.**

Therefore **conversion requires winning, not losing.** You cannot throw yourself
onto the stronger team by giving up; deliberately dying just kills you. The
exploit is impossible by construction and needs no scoring rule to police it.

---

## SOULS AND GHOSTS

NOT the same system as the co-op mode's ghost familiar. Do not merge them.

**THERE IS NO REVIVAL IN THIS MODE. Ghost is your final state.** Once you die you
never hold a body again, no matter what you achieve as a ghost.

Death has two shapes:

**Killed by someone.** Your soul binds to **your killer**, or to the **owner of the
zombie** that killed you, since zombies are spells and spells belong to their
caster. As a bound soul you help your host and corrupt them the whole time. Two
things end the binding:
- **your host dies** → you go free
- **you convert your host** → you go free, having flipped an enemy to your side

**Killed by nothing** (environment, suicide, team kill). You become a free roaming
ghost immediately, with no binding phase at all.

### What a ghost does

Roam, spectate, and **possess spells**. Since zombies are spells, **ghosts can
possess zombies.** That is the mayhem: a dead player picks up a body that is not a
body and goes and makes trouble for the living.

This is where possession lives now. It was cut from the living acolyte's kit on
purpose, because arrows already command zombies and a second control mode was one
thing too many. On a ghost it is the opposite: it is the only thing they have, so
it costs no added complexity and gives the dead something real to do.

**The incentive this creates:** being murdered is better than dying stupidly. A
killed player gets a bound phase in which they can flip an enemy to their team,
which is the largest single swing in the mode, and only then go free. Dying to a
ledge skips the flip entirely. So the environment stays genuinely feared, suicide
to shed a soul throws away your one shot at converting somebody, and a team kill
is a favour nobody wants to receive.

---

## WINNING

Four endings, no overlap:

1. every acolyte gone → **wizards win**
2. every wizard converted → **acolytes win**
3. timer hits zero, pot corrupted → **acolytes win**
4. timer hits zero, pot clean → **wizards win**

The cauldron does **not** need its own win condition. Corrupting it means wizards
cannot purge, which means they convert, which is ending 2. One causal chain
instead of a fourth rule.

The timer being decided by the pot's state is what stops both sides stalling:
acolytes cannot corrupt once and hide, because wizards will heal it; wizards
cannot turtle, because sitting there does not clean it.

---

## MAP

- **centre**: the cauldron, and where wizards spawn
- **acolytes**: spawned randomly, out of sight, far enough that wizards cannot
  reach them quickly
- **both sides spawn at the same time.** No hunter wait period, unlike the genre
  norm, because wizards have real work at second zero: absorbing runes, securing
  ore, setting up the pot

**Sizes:** small is a bit bigger than the Lobby. Medium is 1.5x to 2x that. Large
is 1.5x to 2x medium and wants more players and more time. Nothing needs to be
bigger than a Counter-Strike or Meccha Chameleon map.

**This mode does not need bespoke level design.** It can be dropped into
environments that already exist, which is its single biggest production advantage
over the co-op raid. What a map needs:

- **prop density and repetition.** Several of each object, or transforming into a
  chair in a room with one chair is not hiding
- **a real centre.** Radial maps work. Linear ones (streets, corridors, canyons)
  do not, because "the middle" has only two approaches
- **sightlines**, so zombie traffic can be read across a space. Warrens of small
  rooms turn guided hunting back into blind searching
- **spread ore**, which is the only thing that forces wizards to leave the pot
- **colliders and pathing** per map (`EnvironmentTools` has "Prepare Imported
  Packs")
- licence check before any downloaded environment ships

**The middle deserves the most attention**, because every round ends there. When
the pot is corrupted the mode becomes a retake, exactly like a Counter-Strike
bomb plant, and it needs real approaches and cover.

---

## HOW THE WIZARD IS FORCED TO PLAY

Three mechanics do one job, which is keeping wizards off the cauldron and engaged:

- ranged zombies hit the pot from a distance, so wizards must hold a perimeter
  rather than a point
- zombies strike anyone who turns their back, so wizards cannot run past them
- killing a zombie makes its acolyte whistle, so the fight they are forced into
  pays them information

Wizards also get a real strategic fork out of the soul rule: **hunt** for ending 1,
fast but every kill brings you closer to turning, or **defend** for ending 4, slow
and safe but betting on the clock and your ore.

Acolytes get the mirror of it: killing wizards does not advance their win
condition and it corrupts them, so their zombies should distract rather than
farm. This keeps the mode about the pot instead of degenerating into deathmatch.

---

## STILL OPEN

Nothing below is ruled. Do not implement any of it as though it were.

1. **Does conversion relocate you?** Turning in the middle of the enemy camp with
   an empty grimoire may be a guaranteed execution rather than a scramble.
2. **Does a corruption cloud outlive its zombie?** Lingering zones can fence
   wizards off their own pot.
3. **Win attribution.** Converted players change teams; whether the team standing
   at the end wins for everyone on it was not finally ruled.
4. **Can BOUND souls possess spells, or only free ghosts?** He ruled that ghosts
   possess spells right after describing the free-roaming state, so free ghosts
   certainly can. Whether a soul still attached to its host can is unstated.

## NUMBERS TO TUNE, NOT TO GUESS

All of these belong in `sz_tuning.json` from day one so they can be changed
between rounds with people sitting in the lobby. `DrawingConfig` already does
this; follow the same pattern. `RunStats.cs` should log the active settings with
each finished round, or "that version felt better" is six people's memory.

- **corruption evaporation time.** The single most important number in the mode.
  Aim it so ONE acolyte cannot hold the pot alone but TWO working together can
- kills required to convert
- zombie lifetime, and zombies per summon
- round timer length (this is the acolyte difficulty dial)
- starting acolyte count and ratio
- acolyte spawn distance, and scattered versus grouped
- map crossing time: measure walk speed, target roughly 20 to 30 seconds
