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

### Acolyte controls: SHAPE MODE replaces POSE MODE

Acolytes cannot draw on their own bodies, so they have no use for pose mode. **That
whole mode is swapped for shape mode, reusing the same keys.**

- **1, 2, 3 ...** switch instantly to a form you have collected. Same slot layout
  the wizard's pose mode already uses, so it is the existing system with a
  different payload rather than a new one.
- **F** returns you to your own body, in third person.
- **Tab** goes to first person, always in your body, holding wand and grimoire.
- **Acolytes have no R modes at all.** R is free on this class.

That gives three states, and they form a clean ladder of exposure:

| state | you can | you risk |
|---|---|---|
| in a shape, 3rd person | move, hide | nothing much |
| your body, 3rd person | move, look around | being seen |
| your body, 1st person | draw and summon | everything |

The most powerful thing an acolyte does is also the thing that puts them in the
most exposed state, which is the same law the wizard lives under.

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

**They live at least a minute**, because they have to walk somewhere to matter.
This is NOT the same clock as the seal: every seal produces for a flat 10 seconds,
and what it produced then lives its own life. Two clocks, never conflate them.

Summoned by drawing zombie icons plus a direction arrow **inside one seal**. The
arrow commands where they go, so a single drawing is the whole order. Uses the
existing arrow glyph; corrupt ink is what makes an arrow command the dead instead
of pushing matter. **No new glyphs.**

**One drawing carries five channels, all of them existing spell grammar:**

| what you draw | what it sets |
|---|---|
| number of zombie icons | how many |
| **seal side count** | **which VARIANT**, e.g. smarter or dumber |
| rune-to-seal scale | how big they are |
| which direction of the rune pair | melee or ranged |
| the arrow | where they go |

**Seal sides are a SELECTOR, not a magnitude.** They pick a different type of the
same thing, per rune. They do NOT set how long anything lives. (Seal duration,
which is edges x DurationPerEdge, is how long the SEAL keeps producing. What it
produces has its own separate lifetime. Two clocks, never conflate them.)

Scope rule for variants: **a variant that is a set of numbers is nearly free; a
variant that needs its own model is not.** Smart and dumb zombies share a mesh and
differ only in sensor range, reaction rate, field of view and avoidance strength.
Keep it that way and the variant system costs almost nothing.

Readability comes for free here: a dumb zombie walking into a wall is its own
tell, so no visual difference is strictly required.

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

## THE LOBBY IS THE TUTORIAL

Ruled Aug 6. In the lobby you **pick your side and can switch freely**, and
switching lets you test that side's whole kit. Zombies wander in and attack the
centre cauldron, which is **immortal**, so nothing happens. **Everyone in the lobby
is immortal too**, so nothing matters.

Why this earns its place: asymmetric games rot when players only ever learn one
side. A free switch in the lobby means everybody sees both kits before they are
asked to play against them, and it teaches by play rather than by text, which is
the standing rule. It also converts ready-up dead time into practice.

**You start the lobby with an EMPTY grimoire**, exactly as you start a round, and
earn everything from a display of **immortal sources** standing there waiting to be
absorbed. Absorbing one does not consume it, so every player can absorb everything,
with no queue and no three second wait.

That split is deliberate: **the lobby teaches the verbs, the round teaches the
economy.** You learn what the twelve runes do and how the two kits feel by trying
all of them; you learn scarcity, prioritisation and denial only where it costs
something.

Three things the lobby needs that a real round does not:

- **Lobby sources are immortal and re-absorbable.** In a round, absorbing consumes
  the source and scanning is per instance. In the lobby that would leave a
  practising acolyte dry within a minute and make twelve players queue for one
  torch. The lobby needs the opposite rule.
- **A zombie cap.** Twelve immortal players summoning freely, with zombies living
  a minute and nothing able to kill them, fills the lobby fast. That is both a
  readability and a performance problem.
- **Nothing carries into the round.** Forms scanned and runes absorbed in the lobby
  do not come with you.

**The death mechanics are deliberately NOT taught here** (his ruling). Corruption,
souls and conversion are learned in play, the first time you die. That is
discovered complexity, which his filter keeps, and dying is the best teaching
moment the game has: the player has nothing else to do and their full attention is
on what just happened to them.

Two of the three parts already carry their own warning. Corruption shows on your
wand, greening from the tip, long before it matters. Conversion is visible because
you become the other thing. **The one moment with no build-up is being bound to
your killer**, so that is the beat that has to read instantly and unmistakably,
since there is no second chance to explain it.

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
