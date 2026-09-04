# Steam achievements

Twenty-one achievements, wired in code on Sep 4 2026. The game calls Steam by
API name only; the names, descriptions and icons live on the Steamworks page.
Nothing unlocks until you create them there under the real app id
(`steam_appid.txt` still says 480, the test app, so today Steam refuses every
name with one warning per name in the console).

## Steamworks setup, one row per achievement

Same for every row: Progress Stat `None`, Min and Max `0`, Set By `Client`,
Hidden unchecked. Choose the achieved icon, Upload, choose the unachieved
icon, Upload, then Save the row. Icons are in `ArtSource/Achievements/out/`
(rerun `python ArtSource/Achievements/make_icons.py` after editing
`icons.json`). The display names and descriptions below are drafts; the page
has a language selector for the other eleven languages once the English is final.

| # | API name | Display name | Description | Achieved icon | Unachieved icon |
|---|---|---|---|---|---|
| 1 | SZ_WIN_WIZARDS | Clean pot | win a match as a wizard | SZ_WIN_WIZARDS.jpg | SZ_WIN_WIZARDS_locked.jpg |
| 2 | SZ_WIN_ACOLYTES | Green pot | win a match as an acolyte | SZ_WIN_ACOLYTES.jpg | SZ_WIN_ACOLYTES_locked.jpg |
| 3 | SZ_END_POT_DRY | Bottoms up | drain the cauldron to the last drop | SZ_END_POT_DRY.jpg | SZ_END_POT_DRY_locked.jpg |
| 4 | SZ_END_NO_WIZARDS | Nobody home | every wizard is dead | SZ_END_NO_WIZARDS.jpg | SZ_END_NO_WIZARDS_locked.jpg |
| 5 | SZ_END_GREEN_BELL | Still green | the bell rings on a green pot | SZ_END_GREEN_BELL.jpg | SZ_END_GREEN_BELL_locked.jpg |
| 6 | SZ_END_CLEAN_BELL | Still clean | the bell rings on a clean pot | SZ_END_CLEAN_BELL.jpg | SZ_END_CLEAN_BELL_locked.jpg |
| 7 | SZ_END_SWEEP | Spring cleaning | every acolyte dead and the pot clean | SZ_END_SWEEP.jpg | SZ_END_SWEEP_locked.jpg |
| 8 | SZ_TEN_WINS | Regular | win ten matches | SZ_TEN_WINS.jpg | SZ_TEN_WINS_locked.jpg |
| 9 | SZ_FIRST_RUNE | First page | learn a rune | SZ_FIRST_RUNE.jpg | SZ_FIRST_RUNE_locked.jpg |
| 10 | SZ_ALL_RUNES | Full grimoire | know all twelve runes in one match | SZ_ALL_RUNES.jpg | SZ_ALL_RUNES_locked.jpg |
| 11 | SZ_FIRST_SPELL | It works | close a seal that casts | SZ_FIRST_SPELL.jpg | SZ_FIRST_SPELL_locked.jpg |
| 12 | SZ_BODY_CAST | Look at me | cast a seal drawn on your own body | SZ_BODY_CAST.jpg | SZ_BODY_CAST_locked.jpg |
| 13 | SZ_DISGUISE | Nobody here | become an object | SZ_DISGUISE.jpg | SZ_DISGUISE_locked.jpg |
| 14 | SZ_RIDE_ZOMBIE | Back seat | drive a zombie as a ghost | SZ_RIDE_ZOMBIE.jpg | SZ_RIDE_ZOMBIE_locked.jpg |
| 15 | SZ_RIDE_GOLEM | Big seat | drive a golem as a ghost | SZ_RIDE_GOLEM.jpg | SZ_RIDE_GOLEM_locked.jpg |
| 16 | SZ_GOLEM_BORN | Proud parent | a golem rose from your spell | SZ_GOLEM_BORN.jpg | SZ_GOLEM_BORN_locked.jpg |
| 17 | SZ_REVIVE_FRIEND | Get up | revive a friend | SZ_REVIVE_FRIEND.jpg | SZ_REVIVE_FRIEND_locked.jpg |
| 18 | SZ_CAME_BACK | Not today | come back from the dead | SZ_CAME_BACK.jpg | SZ_CAME_BACK_locked.jpg |
| 19 | SZ_FAT_BOUNCE | Boing | bounce off a wall while fat | SZ_FAT_BOUNCE.jpg | SZ_FAT_BOUNCE_locked.jpg |
| 20 | SZ_POISON_POT | Something in the brew | your side turned the pot green | SZ_POISON_POT.jpg | SZ_POISON_POT_locked.jpg |
| 21 | SZ_CLEAN_POT | Scrubbed | your side cleaned a green pot | SZ_CLEAN_POT.jpg | SZ_CLEAN_POT_locked.jpg |

## After the rows

1. Steamworks Settings, Publish, Prepare for Publishing, Publish to Steam.
   Achievements live in the app config, not the store page.
2. Store page, Basic Info, Supported Features: tick Steam Achievements.
3. `steam_appid.txt` from 480 to 5050950, then test in Play mode with Steam
   running. `Spelly Zombie/Studio/Steam - Reset achievements (this account)`
   clears them between tests; `Print achievement API names` lists the names.

## Who earns what

Wins and endings go to the winning side only. Team deeds (green pot, clean
pot) unlock for everyone on that side. Everything else is personal.

## Where the hooks are

- Endings: `RoundDirector.Win` (host) and the round state message on clients,
  which now carries the ending code.
- Runes: `Grimoire.UnlockRune` and `UnlockRemote`.
- Spells: `DrawingWorld.OnSealEnded` (host) and the seal end message on
  clients, which now carries the owner. Body seals: `DrawingWorld.CreateSeal`.
- Disguise: `ShapeShift.BecomeObject`. Rides: `GhostState`. Revive:
  `GhostState.AddGhostRevive` (the rescuer) and
  `SimpleFPSController.Revive` (the rescued). Fat bounce:
  `SimpleFPSController.OnControllerColliderHit`.
- Pot and golems: `Achievements.Tick`, polled from `RoundDirector.Update`.
  Golem snapshots now carry the owner so clients know their own golems.

Not in this set: kills. The world only remembers who finished a thing by its
element id, and mapping that back to an enemy player on every machine needs
its own pass.
