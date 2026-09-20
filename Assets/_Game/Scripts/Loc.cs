using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Every player-facing string. Loc.T("key"), or Loc.F("key", args) when
    /// it contains {0} placeholders.
    ///
    /// Translations are JSON in StreamingAssets/Loc, one file per language,
    /// switchable at runtime. A missing file or key falls back to English.
    /// Twelve languages, the Meccha Chameleon set minus Arabic (his call).
    public static class Loc
    {
        static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            // ---- interact prompts (the [E] bar) ----
            ["door.open"] = "open the door",
            ["door.close"] = "close the door",
            ["pickup.weapon"] = "pick up the weapon",
            ["pickup.full"] = "hands full, drop one first",
            ["chest.try"] = "try the mystery chest",
            ["perk.drink"] = "drink {0}",
            ["perk.brewed"] = "{0} is already brewed",
            ["grimoire.open"] = "open the grimoire",
            ["grimoire.close"] = "close the grimoire",
            ["chip.done"] = "done",
            ["carry.down"] = "put it down",

            // ---- one-fact chips, three on screen at most ----
            ["scan.aim"] = "scan it, become it",
            ["absorb.aim"] = "absorb it, learn its rune",
            ["chest.open"] = "open the chest",
            ["chip.grimoire"] = "grimoire",
            ["chip.paint"] = "paint your body",
            ["chip.first"] = "first person",
            ["chip.third"] = "third person",
            ["chip.pose"] = "pose your wizard",
            ["chip.watch"] = "watch your dead",
            ["chip.become"] = "become it again",
            ["chip.melt"] = "melt back to idle",
            ["chip.precise"] = "faster drawing",
            ["chip.erase"] = "erase ink",
            ["chip.absorb"] = "absorb it",

            // ---- loading screen tips, one shown at random per load ----
            ["hint.alt"] = "hold ALT to draw faster",
            ["hint.combine"] = "draw more runes inside of the same seal to combine them",
            ["hint.lift"] = "draw ink on things and press E to lift them",
            ["hint.erase"] = "erasing returns the ink to your wand",
            ["hint.body"] = "press R to paint runes on your own body",
            ["hint.pose"] = "striking a pose can close a body seal and cast it",
            ["hint.size"] = "bigger runes make stronger spells",
            ["hint.touch"] = "lines count as one drawing only when they touch",
            ["hint.declare"] = "the book can name a drawing that reads wrong",
            ["hint.trance"] = "fresh ink puts zombies in a trance",
            ["hint.wake"] = "throw a sleeping spell to wake it",
            ["hint.ghost"] = "the dead rise as ghosts. fly home to your body and a friend can revive you",
            ["hint.doors"] = "doors open when you walk into them",
            ["paint.done"] = "done painting",
            ["paint.pose"] = "strike a pose",
            ["paint.orbit"] = "orbit",
            ["hat.pillar"] = "pick your hat color",
            ["side.pillar"] = "change your side",
            ["hat.done"] = "done",
            ["shape.back"] = "back to yourself",
            ["shape.turn"] = "turns you",
            ["shape.save"] = "saves",
            ["shape.recall"] = "recalls",

            // ---- pause menu and options ----
            ["menu.resume"] = "Resume",
            ["menu.restart"] = "Restart run",
            ["menu.options"] = "Options",
            ["menu.share"] = "Share with friends",
            ["menu.sharecopied"] = "Link copied. Send it to a friend",
            ["menu.quit"] = "Quit",
            ["menu.back"] = "Back",
            ["menu.quit.game"] = "Quit the game?",
            ["menu.quit.lobby"] = "Back to the main menu?",
            ["menu.quit.match"] = "Leave the match for an empty lobby?",
            ["menu.quit.host"] = "You are the host. Everyone else goes home too.",
            ["menu.cancel"] = "Cancel",
            ["menu.mapcreator"] = "Map Creator",
            ["creator.help"] = "Right mouse looks, middle mouse drags the view, {0} flies, Space rises and Ctrl sinks, the wheel moves forward and back. Left click places the picked piece or picks one up to drag it. Delete removes, {1} turns, {2} {3} resize, Ctrl+Z undoes and Ctrl+R redoes.",
            ["creator.timer"] = "Match clock: {0} min",
            ["creator.noclock"] = "Match clock: none",
            ["creator.place"] = "PLACE",
            ["creator.pickone"] = "Pick a piece below, then click the ground",
            ["creator.body"] = "Body: {0}",
            ["creator.rotate"] = "Turn",
            ["creator.bigger"] = "Bigger",
            ["creator.smaller"] = "Smaller",
            ["creator.delete"] = "Delete",
            ["creator.save"] = "Save",
            ["creator.new"] = "New map",
            ["creator.load"] = "SAVED MAPS",
            ["creator.saved"] = "Saved {0}",
            ["maps.title"] = "Maps",
            ["maps.tab.shared"] = "Shared maps",
            ["maps.tab.mine"] = "My maps",
            ["maps.create"] = "New blank map",
            ["maps.back"] = "Back",
            ["maps.pick"] = "Pick a map to see it here",
            ["maps.preview"] = "Preview",
            ["maps.keep"] = "Save",
            ["maps.kept"] = "Saved",
            ["maps.edit"] = "Edit",
            ["maps.share"] = "Share",
            ["maps.page"] = "Open its Steam page",
            ["maps.delete.sure"] = "Delete for good?",
            ["maps.by"] = "By {0}",
            ["maps.info"] = "{0} biomes, {1} creatures, {2} pieces",
            ["maps.none"] = "Nobody has shared a map yet",
            ["maps.nosteam"] = "Shared maps need Steam",
            ["maps.loading"] = "Looking for shared maps",
            ["maps.downloading"] = "Downloading the map",
            ["maps.sharing"] = "Sharing your map",
            ["maps.done.shared"] = "Your map is shared",
            ["maps.failed"] = "That did not work: {0}",
            ["maps.description"] = "A Spelly Zombie map with {0} biomes, made by {1}",
            ["mc.map"] = "Map",
            ["mc.biomes"] = "Biomes",
            ["mc.biome"] = "Biome",
            ["mc.objects"] = "Objects",
            ["mc.spells"] = "Spells",
            ["mc.runes"] = "Runes",
            ["mc.generate"] = "Generate",
            ["mc.hint.idle"] = "Pick a biome or an object, then click the map",
            ["mc.hint.place"] = "Click the map to place {0}. Esc stops",
            ["mc.hint.placemany"] = "Click the map to place {0} as many times as you like. Esc stops",
            ["mc.hint.biome"] = "Drag {0} to move it, the gold balls size it, the tall handle lifts and lowers it",
            ["mc.hint.object"] = "Drag it to move it, {0} turns it, {1} {2} size it, Delete removes it",
            ["mc.hint.stale"] = "The ground is out of date: press Generate",
            ["mc.hint.preview"] = "Right mouse looks around, {0} flies",
            ["mc.name"] = "Name",
            ["mc.groundhead"] = "Ground",
            ["mc.newseed"] = "Another ground",
            ["mc.frombase"] = "Start from the island's biomes",
            ["mc.picture"] = "Saving takes the map's picture from where the camera looks",
            ["mc.biomes.note"] = "Pick a biome, then click the map to place it",
            ["mc.onmap"] = "On this map: {0}",
            ["mc.kind.ground"] = "Ground",
            ["mc.kind.liquid"] = "Liquid",
            ["mc.shape"] = "Box",
            ["mc.width"] = "Width",
            ["mc.depth"] = "Length",
            ["mc.height"] = "Height",
            ["mc.bottom"] = "Bottom height",
            ["mc.layer"] = "Layer",
            ["mc.core"] = "Middle nobody can cut",
            ["mc.rough"] = "Bumpiness",
            ["mc.hills"] = "Hill size",
            ["mc.paths"] = "Paths through it",
            ["mc.pathbend"] = "How much paths bend",
            ["mc.brush"] = "Ground look",
            ["mc.pathbrush"] = "Path look",
            ["mc.none"] = "None",
            ["mc.fill"] = "Things inside",
            ["mc.spacing"] = "Room for each thing, metres",
            ["mc.slope"] = "Steepest ground, degrees",
            ["mc.props"] = "Things scattered here: {0}",
            ["mc.sources"] = "Glowing sources: {0}",
            ["mc.minsources"] = "Fewest glowing sources",
            ["mc.maxsources"] = "Most glowing sources",
            ["mc.walllight"] = "Light on house walls",
            ["mc.cauldron"] = "Cauldron",
            ["mc.landmark"] = "Landmark",
            ["mc.imposed"] = "Forced on everything here",
            ["mc.allowed"] = "Limits here",
            ["mc.healing"] = "Healing speed",
            ["mc.spawn"] = "Wizard start",
            ["mc.wizardhome"] = "Wizards start here",
            ["mc.spread"] = "How spread out",
            ["mc.byobjects"] = "Only around its things",
            ["mc.full"] = "Full within, metres",
            ["mc.reach"] = "Gone past, metres",
            ["mc.buoyancy"] = "Floatiness",
            ["mc.surface"] = "Surface",
            ["mc.duplicate"] = "Copy",
            ["mc.team"] = "Fights for",
            ["mc.team.wild"] = "Nobody",
            ["mc.team.wizards"] = "Wizards",
            ["mc.team.acolytes"] = "Acolytes",
            ["mc.size"] = "Size",
            ["mc.delay"] = "First one after, seconds",
            ["mc.every"] = "Again every, seconds (0 = once)",
            ["mc.count"] = "How many each time",
            ["mc.maxalive"] = "Most alive at once (0 = no limit)",
            ["mc.forever"] = "Lives until killed",
            ["mc.spells.note"] = "These spells belong to this map. The main game keeps its own",
            ["mc.spell.new"] = "New spell",
            ["mc.body.zombie"] = "Zombie",
            ["mc.body.golem"] = "Golem",
            ["mc.spell.book"] = "Book",
            ["mc.side.wizard"] = "Wizard",
            ["mc.side.acolyte"] = "Acolyte",
            ["mc.summonedby"] = "Summoned by",
            ["mc.summon.none"] = "Nothing summons this yet",
            ["mc.bornas"] = "Born as",
            ["mc.cando"] = "Can do",
            ["mc.charge"] = "Charge",
            ["mc.casts"] = "Casts",
            ["mc.conditions"] = "Conditions",
            ["mc.places"] = "Lock as a biome",
            ["mc.effects"] = "Effects",
            ["mc.onlyliving"] = "Only things with a mind",
            ["mc.area"] = "Area",
            ["mc.spellshape"] = "Shape",
            ["mc.material"] = "Material",
            ["mc.look.wobble"] = "Liquid wobble",
            ["mc.look.wobblespeed"] = "Liquid speed",
            ["mc.look.swirl"] = "Gas swirl",
            ["mc.look.swirlspeed"] = "Swirl speed",
            ["mc.look.turbulence"] = "Turbulence",
            ["mc.look.bubbles"] = "Bubbles",
            ["mc.look.bubblesize"] = "Bubble size",
            ["mc.look.bubblerise"] = "Bubble rise",
            ["mc.look.holes"] = "Break-up",
            ["mc.look.holesize"] = "Hole size",
            ["mc.look.rim"] = "Rim glow",
            ["axis.0"] = "Temperature",
            ["axis.1"] = "Light",
            ["axis.2"] = "Pressure",
            ["axis.3"] = "Balance",
            ["axis.4"] = "State",
            ["axis.5"] = "Affinity",
            ["axis.6"] = "Strength",
            ["axis.7"] = "Mind",
            ["axis.8"] = "Courage",
            ["axis.9"] = "Clones",
            ["mc.runes.note"] = "These runes belong to this map. Drawings you keep teach the map to read them",
            ["mc.rune.new"] = "New rune",
            ["mc.emoji"] = "Emoji",
            ["mc.clear"] = "Clear",
            ["mc.pushes"] = "Pushes",
            ["mc.drawing"] = "Drawing",
            ["mc.pad.note"] = "Left mouse draws, right mouse erases",
            ["mc.undo"] = "Undo",
            ["mc.redo"] = "Redo",
            ["mc.keep"] = "Keep this drawing",
            ["mc.kept"] = "Kept drawings. Pick one to work on it, the plus starts a new one, X removes one",
            ["mc.reads"] = "Reads as {0}",
            ["mc.reads.none"] = "Not readable yet",
            ["clip.failed"] = "Recording did not work on this PC",
            ["clip.disk"] = "Not enough free disk space to record",
            ["photo.color"] = "Color",
            ["photo.color.own"] = "Its own color",
            ["photo.anims"] = "Animations",
            ["photo.anim.play"] = "Play",
            ["photo.anim.pause"] = "Pause",
            ["photo.anim.note"] = "Pick one to play it. Pause and use the time slider to hold any moment.",
            ["menu.photobooth"] = "Photo Booth",
            ["photos.title"] = "Photo Booth",
            ["photos.newphoto"] = "New photo",
            ["photos.newfolder"] = "New folder",
            ["photos.pick"] = "Pick a photo or a folder to see it here",
            ["photos.count"] = "{0} photos",
            ["photos.open"] = "Open",
            ["photos.folder"] = "Folder",
            ["photos.nofolder"] = "No folder",
            ["photos.folder.name"] = "Folder name",
            ["photos.folder.new"] = "New folder name",
            ["photos.folder.make"] = "Make the folder",
            ["photos.rename"] = "Rename",
            ["photos.folder.delete"] = "Delete folder",
            ["photos.folder.full"] = "Only an empty folder can be deleted",
            ["photos.taken"] = "That name is taken",
            ["photos.files"] = "Open the photos folder",
            ["photo.stored"] = "Saved {0}",
            ["photo.add"] = "Add",
            ["photo.ink"] = "Ink",
            ["photo.look"] = "Light and look",
            ["photo.photo"] = "Photo",
            ["photo.take"] = "Take the photo",
            ["photo.new"] = "New photo",
            ["photo.help"] = "Right mouse looks, middle mouse drags the view, {0} flies, Space rises and Ctrl sinks, the wheel moves forward and back. Left click picks a thing up to drag it. Ctrl+Z undoes and Ctrl+R redoes.",
            ["photo.hint.idle"] = "Pick something in Add, then click the ground",
            ["photo.hint.place"] = "Click the ground to put down {0} as many times as you like. Esc stops",
            ["photo.hint.picked"] = "Drag it to move it, Shift drag lifts it, {0} turns it, {1} {2} size it, Delete removes it",
            ["photo.hint.pose"] = "Drag a hand or a foot. Shift drag turns one bone, the wheel twists it. Esc when done",
            ["photo.hint.ink"] = "Draw on anything you placed. Esc when done",
            ["photo.taken"] = "Photo saved: {0}",
            ["photo.failed"] = "The photo did not work",
            ["photo.add.note"] = "Pick one, then click the ground. Pick it again to stop.",
            ["photo.add.characters"] = "Characters",
            ["photo.add.creatures"] = "Creatures",
            ["photo.add.areas"] = "Areas",
            ["photo.add.effects"] = "Effects",
            ["photo.add.pieces"] = "Island pieces",
            ["photo.add.inside"] = "Inside houses",
            ["photo.add.empty"] = "Nothing here yet",
            ["photo.lift"] = "Off the ground",
            ["photo.tilt"] = "Tilt",
            ["photo.roll"] = "Roll",
            ["photo.time"] = "Time",
            ["photo.side"] = "Side",
            ["photo.outfit.mine"] = "My outfit",
            ["photo.outfit.shuffle"] = "Shuffle outfit",
            ["photo.hat"] = "Hat color",
            ["photo.hat.hue"] = "Hue",
            ["photo.hat.sat"] = "Color strength",
            ["photo.hat.light"] = "Brightness",
            ["photo.hat.mine"] = "My hat color",
            ["photo.eyes"] = "Eyes",
            ["photo.mood.neutral"] = "Calm",
            ["photo.mood.scared"] = "Scared",
            ["photo.mood.wowed"] = "Wowed",
            ["photo.mood.mad"] = "Mad",
            ["photo.mood.dizzy"] = "Dizzy",
            ["photo.gaze"] = "Looking",
            ["photo.gaze.camera"] = "At the camera",
            ["photo.gaze.ahead"] = "Straight ahead",
            ["photo.body"] = "Body",
            ["photo.pose.start"] = "Pose it",
            ["photo.pose.stop"] = "Done posing",
            ["photo.pose.relax"] = "Stand normal",
            ["photo.pose.saved"] = "Saved poses",
            ["photo.pose.none"] = "No saved poses yet. Save some in pose mode.",
            ["photo.ink.note"] = "Draw on anything you placed. The ink rides the part it lands on.",
            ["photo.ink.draw"] = "Draw",
            ["photo.ink.stop"] = "Stop drawing",
            ["photo.ink.ink"] = "Wizard ink",
            ["photo.ink.green"] = "Acolyte ink",
            ["photo.ink.gold"] = "Seal gold",
            ["photo.ink.blue"] = "Rune blue",
            ["photo.ink.white"] = "White",
            ["photo.ink.black"] = "Black",
            ["photo.ink.red"] = "Red",
            ["photo.ink.width"] = "Thickness",
            ["photo.ink.undo"] = "Take back the last line",
            ["photo.ink.wipepicked"] = "Wipe the picked one",
            ["photo.ink.wipe"] = "Wipe all ink",
            ["photo.light"] = "Light",
            ["photo.sun.turn"] = "Sun turn",
            ["photo.sun.height"] = "Sun height",
            ["photo.sun.power"] = "Sun strength",
            ["photo.sun.reset"] = "Put the sun back",
            ["photo.rim"] = "Back light",
            ["photo.rim.turn"] = "Back light turn",
            ["photo.picture"] = "Picture",
            ["photo.brightness"] = "Brightness",
            ["photo.contrast"] = "Contrast",
            ["photo.warmth"] = "Warmth",
            ["photo.saturation"] = "Color",
            ["photo.glow"] = "Glow",
            ["photo.vignette"] = "Dark corners",
            ["photo.blur"] = "Background blur",
            ["photo.focus"] = "Sharp at",
            ["photo.focus.picked"] = "Make the picked one sharp",
            ["photo.lens"] = "Lens",
            ["photo.look.reset"] = "Reset the look",
            ["photo.canvas"] = "Canvas",
            ["photo.width"] = "Width",
            ["photo.height"] = "Height",
            ["photo.sizes"] = "Sizes for",
            ["photo.safe.note"] = "The white box is Steam's safe area",
            ["photo.seethrough.note"] = "Steam wants this one with nothing behind",
            ["photo.back"] = "Behind",
            ["photo.back.world"] = "The world",
            ["photo.back.colour"] = "A color",
            ["photo.back.none"] = "Nothing",
            ["photo.back.none.note"] = "Nothing behind: the grey is not in the photo. Glow and blur stay off so the edges stay clean.",
            ["photo.red"] = "Red",
            ["photo.green"] = "Green",
            ["photo.blue"] = "Blue",
            ["photo.saved"] = "Photos in this folder",
            ["photo.saved.none"] = "No saved photos here yet",
            ["photo.size.youtube"] = "YouTube thumbnail",
            ["photo.size.hd"] = "Full HD",
            ["photo.size.4k"] = "4K",
            ["photo.size.square"] = "Square post",
            ["photo.size.portrait"] = "Tall post",
            ["photo.size.story"] = "Story, Reels, Shorts, TikTok",
            ["photo.size.x"] = "X post",
            ["photo.size.steamheader"] = "Steam header",
            ["photo.size.steamsmall"] = "Steam small capsule",
            ["photo.size.steammain"] = "Steam main capsule",
            ["photo.size.steamvertical"] = "Steam vertical capsule",
            ["photo.size.steampage"] = "Steam page background",
            ["photo.size.librarycapsule"] = "Steam library capsule",
            ["photo.size.libraryhero"] = "Steam library hero",
            ["photo.size.librarylogo"] = "Steam library logo",
            ["end.potdry"] = "The pot ran dry",
            ["end.nowizards"] = "No wizard left standing",
            ["end.sweep"] = "Every acolyte caught, the pot still clean",
            ["end.greenbell"] = "Time's up and the pot is green",
            ["end.cleanbell"] = "Time's up and the pot is clean",
            ["end.bossdown"] = "The boss fell",
            ["end.everyonedown"] = "Everyone went down",
            ["end.timeup"] = "Time's up",
            ["mc.official"] = "This map comes with the game. Give it a new name to save your own copy.",
            ["maps.official"] = "Comes with the game",
            ["mc.spell.kind"] = "Kind",
            ["mc.kind.spell"] = "Spell",
            ["mc.kind.summon"] = "Summon",
            ["mc.summons"] = "Summons",
            ["mc.summons.none"] = "Make a creature in the Creature Creator first",
            ["mc.note.summonlook"] = "A summon looks like the creature it raises. Change its look in the Creature Creator.",
            ["mc.note.handles"] = "Drag to turn it, the wheel zooms. Drag a colored square to change its shape.",
            ["mc.shape.reset"] = "Reset shape",
            ["mc.build.height"] = "Height",
            ["mc.build.width"] = "Width",
            ["mc.build.head"] = "Head",
            ["mc.build.arms"] = "Arms",
            ["mc.build.legs"] = "Legs",
            ["mc.note.size"] = "Times its body's own size. A seal or a placement makes it bigger or smaller on top.",
            ["mc.teams"] = "Teams",
            ["mc.teams.players"] = "Players",
            ["mc.teams.split"] = "Two teams",
            ["mc.teams.together"] = "One team",
            ["mc.counts.everyone"] = "Players count",
            ["mc.counts.wizards"] = "Wizards count",
            ["mc.counts.acolytes"] = "Acolytes count",
            ["mc.counts.environment"] = "Environment counts",
            ["mc.note.teams"] = "Teams that count play to win. The last one standing wins. The environment stands while a boss lives, and wins when time runs out.",
            ["mc.note.noboss"] = "The environment counts but has no boss yet. Turn on Boss for a creature in the Creature Creator and place it.",
            ["mc.boss"] = "Boss",
            ["mc.note.boss"] = "Everyone sees a boss's health. The environment stands while a boss lives.",
            ["round.everyone"] = "EVERYONE WINS",
            ["round.bosswins"] = "THE BOSS WINS",
            ["round.nobody"] = "NOBODY WINS",
            ["round.beatboss"] = "BEAT THE BOSS",
            ["round.playaround"] = "JUST HAVE FUN",
            ["mc.creatures.open"] = "Creature Creator",
            ["mc.creatures.note"] = "These creatures belong to this map. Place them from the Objects window or raise them with a summon.",
            ["mc.creature.new"] = "New creature",
            ["mc.creature.sure"] = "Sure? The placed ones go too",
            ["mc.creature.none"] = "Pick a creature or make a new one",
            ["mc.creature.edit"] = "Edit this creature",
            ["mc.creature.body"] = "Body",
            ["mc.creature.preview"] = "Drag to turn it, the wheel zooms. The color follows its numbers.",
            ["mc.behaviour"] = "Behavior",
            ["mc.behaviour.roams"] = "Roams",
            ["mc.behaviour.hunts"] = "Hunts",
            ["mc.behaviour.guards"] = "Guards",
            ["mc.behaviour.skittish"] = "Skittish",
            ["mc.note.roams"] = "Wanders and fights what it notices. A zombie still runs from wands.",
            ["mc.note.hunts"] = "Goes after every enemy it notices and never runs away.",
            ["mc.note.guards"] = "Stays near where it stood up, fights whoever comes close and never runs.",
            ["mc.note.skittish"] = "Runs from wands and spells, and fights when cornered.",
            ["mc.guardrange"] = "Guard distance",
            ["mc.note.golemcharge"] = "A golem always charges at what it fights.",
            ["mc.note.creaturearea"] = "What a golem drops around itself as it walks. A zombie body drops nothing.",
            ["mc.back.creature"] = "Back to the creature",
            ["mc.needbiome"] = "A map needs at least 1 biome",
            ["mc.back.menu"] = "Back to the menu",
            ["mc.back.spell"] = "Back to the spell",
            ["mc.objects.creatures"] = "Creatures",
            ["mc.objects.inside"] = "Inside houses",
            ["mc.shape.wearing"] = "This spell wears",
            ["mc.summon.addrune"] = "Add a rune",
            ["mc.area.open"] = "Open area",
            ["mc.area.look"] = "Look",
            ["mc.note.look"] = "A fire effect, a lightning effect, a rock, a trail. Anything.",
            ["mc.note.areatint"] = "Colored by the spell that carries it.",
            ["mc.area.nolook"] = "No look yet. Pick one on the right",
            ["mc.emoji.pick"] = "Pick an emoji",
            ["mc.keep.update"] = "Keep the changes",
            ["mc.page.brush"] = "Brush size",
            ["mc.page.stamp"] = "Stamp the rune on click",
            ["mc.page.stampsize"] = "Stamp size",
            ["mc.page.stampturn"] = "Stamp turn",
            ["mc.page.noglyph"] = "This rune has no drawing to stamp yet",
            ["mc.back.map"] = "Back to the map",
            ["mc.preview.note"] = "Drag to turn it, the wheel zooms. Drag a square to reshape the body. The color follows the sliders.",
            ["mc.shape.save"] = "Save shape",
            ["mc.shape.delete"] = "Delete shape",
            ["mc.spell.none"] = "Make a spell or pick one from the list",
            ["mc.note.summoned"] = "The runes a seal must hold to raise this. The same rune twice means twice, so two of one rune can raise what one cannot.",
            ["mc.summon.all"] = "All twelve",
            ["mc.note.bornas"] = "Its natural state. It drifts from here like anything in the world: a zombie born hot is at home in fire.",
            ["mc.casts.all"] = "Every spell",
            ["mc.note.conditions"] = "The conditions that need to be met for the spell to exist: what it is, and what it gives to whatever it touches.",
            ["mc.note.effects"] = "Byproducts a spell may carry, never needed to make it. They ride the same numbers and land on whatever the spell touches.",
            ["mc.verdict.nothing"] = "Every axis is zero. This spell is nothing.",
            ["mc.verdict.reach"] = "When something's numbers reach these, it becomes this.",
            ["mc.verdict.area"] = "It carries an area, which outlives it.",
            ["mc.verdict.locked"] = "Its locked axes hold against the world and do not spend on impact.",
            ["mc.verdict.spends"] = "It spends itself on whatever it touches.",
            ["mc.verdict.physical"] = "Force can destroy it, and what it still holds scatters.",
            ["mc.verdict.ghost"] = "Force cannot touch it. It only goes out when its numbers run down.",
            ["mc.note.area"] = "The effect that rides on this spell. It works from the same numbers.",
            ["mc.area.new"] = "New area",
            ["mc.area.named"] = "{0} area",
            ["mc.area.loadspell"] = "Load a spell",
            ["mc.note.loadspell"] = "The area becomes that spell: its numbers replace what the spell hands down.",
            ["mc.area.trailwidth"] = "Trail width",
            ["mc.area.traillasts"] = "Trail lasts, seconds",
            ["mc.area.start"] = "Where it starts",
            ["mc.note.start"] = "It rushes back to the spell from here. Put Y at 20 and it falls from the sky.",
            ["mc.area.spreading"] = "Spreading",
            ["mc.note.spreading"] = "Appears again on nearby things that meet the same condition.",
            ["mc.area.delete"] = "Delete area",
            ["mc.note.material"] = "Everything starts still. Move a slider to add movement.",
            ["mc.page"] = "Page",
            ["mc.page.note"] = "The page this rune shows in the grimoire. Left mouse paints, right mouse paints paper. It is saved with the map",
            ["mc.side"] = "Side",
            ["mc.side.note"] = "The emoji and the page belong to this side. An acolyte with none of its own shows the wizard's.",
            ["mc.pushes.note"] = "What drawing this rune adds to the seal's numbers, one axis or several. The spells decide what that becomes.",
            ["mc.rune.none"] = "Make a rune or pick one from the list",
            ["opt.sens"] = "Look sensitivity: {0}",
            ["opt.uiscale"] = "UI size: {0}%",
            ["opt.volume"] = "Volume: {0}%",
            ["opt.language"] = "Language: {0}",
            ["opt.immersive.on"] = "Immersive mode: ON",
            ["opt.immersive.off"] = "Immersive mode: OFF",
            ["opt.immersive.hint"] = "no HUD at all. for players who know the game",
            ["opt.mic"] = "Microphone: {0}",
            ["opt.mic.default"] = "default",
            ["opt.mute"] = "{0}: mute",
            ["opt.unmute"] = "{0}: unmute",
            ["opt.nobody"] = "nobody else here to mute",
            ["opt.mic.title"] = "Microphone",
            ["opt.mic.open"] = "Open mic",
            ["opt.mic.ptt"] = "Hold V",
            ["opt.mic.off"] = "Muted",
            ["opt.tab.game"] = "Game",
            ["opt.tab.video"] = "Video",
            ["opt.tab.audio"] = "Audio",
            ["opt.resolution"] = "Resolution: {0}",
            ["opt.resolution.title"] = "Resolution",
            ["opt.display"] = "Display",
            ["opt.display.full"] = "Fullscreen",
            ["opt.display.borderless"] = "Borderless",
            ["opt.display.windowed"] = "Windowed",
            ["opt.quality"] = "Quality",
            ["opt.low"] = "Low",
            ["opt.medium"] = "Medium",
            ["opt.high"] = "High",
            ["opt.textures"] = "Textures",
            ["opt.shadows"] = "Shadows",
            ["opt.effects"] = "Effects",
            ["opt.motionblur"] = "Motion blur",
            ["opt.aa"] = "Antialiasing",
            ["opt.fps"] = "Frame limit",
            ["opt.vsync"] = "VSync",
            ["opt.off"] = "Off",
            ["opt.on"] = "On",
            ["opt.music"] = "Music: {0}%",
            ["opt.sfx"] = "Sounds: {0}%",
            ["menu.leave"] = "Leave lobby",
            ["menu.delete"] = "Delete lobby",
            ["menu.play"] = "PLAY",
            ["lobby.readycall"] = "ready check. B yes, C no",
            ["lobby.ready.on"] = "READY {0}/{1}",
            ["lobby.ready.off"] = "READY {0}/{1}. B when ready",

            // ---- the book stand (host controller) ----
            ["stand.title"] = "the book stand",
            ["stand.hostprivate"] = "create private lobby (invite only)",
            ["stand.hostpublic"] = "CREATE PUBLIC LOBBY",
            ["stand.pw"] = "password (optional)",
            ["stand.code"] = "entry code (optional)",
            ["stand.hint"] = "walk away to close",
            ["stand.map"] = "change map",
            ["stand.share"] = "acolytes at least {0}%",
            ["stand.setcode"] = "set code",
            ["stand.readycall"] = "ready check",
            ["stand.invite"] = "invite friends",
            ["stand.start"] = "START",
            ["stand.waiting"] = "waiting for ready",
            ["stand.delete"] = "delete lobby",
            ["stand.kick"] = "kick",
            ["stand.ban"] = "ban",
            ["stand.banned"] = "banned: {0}",
            ["stand.unban"] = "unban",
            ["stand.name"] = "lobby name",
            ["stand.size"] = "size {0}",
            ["stand.nocap"] = "no cap",
            ["stand.heavy"] = "your connection carries everyone",
            ["stand.region"] = "region: {0}",
            ["stand.tab.host"] = "HOST",
            ["stand.tab.join"] = "JOIN",
            ["stand.settings"] = "Settings",
            ["stand.regions"] = "Regions",
            ["stand.langs"] = "Languages",
            ["stand.behaviors"] = "Behaviors",
            ["stand.duration"] = "time {0} min",
            ["stand.setpw"] = "set password",
            ["stand.hosting"] = "HOSTING your lobby",
            ["stand.players"] = "Players",
            ["filter.all"] = "all",

            // ---- lobby browser ----
            ["browse.refresh"] = "refresh",
            ["browse.join"] = "JOIN",
            ["browse.locked"] = "(password)",
            ["browse.none"] = "no lobbies found. host one!",
            ["browse.needpw"] = "this lobby wants a password",
            ["browse.cancel"] = "cancel",
            ["region."] = "any region",
            ["region.eu"] = "Europe",
            ["region.na"] = "North America",
            ["region.sa"] = "South America",
            ["region.asia"] = "Asia",
            ["region.oce"] = "Oceania",
            ["region.mea"] = "Middle East & Africa",
            ["tag.welcome"] = "everyone welcome",
            ["tag.beginners"] = "beginners welcome",
            ["tag.casual"] = "casual fun",
            ["tag.tryhard"] = "try hards",
            ["tag.mic"] = "mic on",
            ["tag.quiet"] = "quiet ok",

            // ---- the book finishing drawings ----
            ["seal.norune"] = "no rune here to seal. aim at one of your runes",
            ["seal.noink"] = "not enough ink for the seal",
            ["rune.noink"] = "not enough ink to finish the rune",

            // ---- the referee's banners and status line ----
            ["round.safe"] = "THE LOBBY IS SAFE GROUND",
            ["round.versus"] = "WIZARDS vs ACOLYTES",
            ["round.wizards"] = "WIZARDS WIN",
            ["round.acolytes"] = "ACOLYTES WIN",
            ["round.home"] = "{0}. back to the lobby in {1}",
            ["round.pot"] = " · pot {0}%",
            ["round.green"] = " · the pot is GREEN",
            ["round.potopens"] = " · pot opens in {0}",
            ["round.inkflight"] = " · ink in flight {0}",
            ["gate.accepts"] = "THE GATE ACCEPTS",

            // ---- the connection corner and the Steam layer's own words ----
            ["net.hostleft"] = "THE HOST LEFT",
            ["net.hosting"] = "● HOSTING, {0} player(s)",
            ["net.connected"] = "● CONNECTED, {0} player(s)",
            ["net.map"] = "MAP: {0}",
            ["net.maplikes"] = "MAP: {0} · ♥{1}",
            ["steam.offline"] = "Steam not running, offline & LAN only",
            ["steam.ready"] = "Steam ready: {0}",
            ["steam.leavefirst"] = "leave your lobby first",
            ["steam.ping"] = "your ping to that host is {0}ms, lobby allows {1}",
            ["steam.joining"] = "joining…",
            ["steam.deleted"] = "lobby deleted",
            ["steam.notrunning"] = "Steam not running",
            ["steam.creating"] = "creating lobby…",
            ["steam.failed"] = "lobby failed: {0}",
            ["steam.noenter"] = "couldn't enter the lobby",
            ["steam.nohost"] = "lobby has no host, try again",
            ["steam.connecting"] = "joined, connecting…",
            ["steam.private"] = "PRIVATE LOBBY, invite friends",
            ["steam.public"] = "PUBLIC LOBBY, listed",

            // ---- main menu ----
            ["menu.tagline"] = "draw fast. die funny.",
            ["menu.close"] = "Close",
            ["chip.pages"] = "turn the pages",
        };

        static Dictionary<string, string> _active; // loaded translation (null = English)
        static bool _loaded;

        public static string T(string key)
        {
            Load();
            if (_active != null && _active.TryGetValue(key, out var s)) return s;
            return _en.TryGetValue(key, out var e) ? e : key;
        }

        public static string F(string key, params object[] args)
            => string.Format(T(key), args);

        // ---- the languages ----
        // Each named in its own script. English is index 0 and compiled in,
        // so the game shows text even with no files on disk.
        public struct Lang
        {
            public string Code, Native;
            public Lang(string code, string native) { Code = code; Native = native; }
        }

        public static readonly Lang[] Languages =
        {
            new Lang("en", "English"),
            new Lang("ja", "日本語"),
            new Lang("zh-CN", "简体中文"),
            new Lang("zh-TW", "繁體中文"),
            new Lang("ko", "한국어"),
            new Lang("es", "Español"),
            new Lang("pt-BR", "Português (BR)"),
            new Lang("fr", "Français"),
            new Lang("de", "Deutsch"),
            new Lang("it", "Italiano"),
            new Lang("ru", "Русский"),
            new Lang("tr", "Türkçe"),
        };

        /// Fired after a language change so open UI can rebuild its labels.
        public static event System.Action Changed;

        /// PlayerPrefs override, else the OS language.
        public static string LanguageCode
        {
            get
            {
                var forced = PlayerPrefs.GetString("sz_lang", "");
                if (!string.IsNullOrEmpty(forced)) return forced;
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Japanese: return "ja";
                    case SystemLanguage.Spanish: return "es";
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified: return "zh-CN";
                    case SystemLanguage.ChineseTraditional: return "zh-TW";
                    case SystemLanguage.Korean: return "ko";
                    case SystemLanguage.French: return "fr";
                    case SystemLanguage.Italian: return "it";
                    case SystemLanguage.German: return "de";
                    case SystemLanguage.Portuguese: return "pt-BR";
                    case SystemLanguage.Russian: return "ru";
                    case SystemLanguage.Turkish: return "tr";
                    default: return "en";
                }
            }
        }

        public static string NativeName(string code)
        {
            foreach (var l in Languages) if (l.Code == code) return l.Native;
            return code;
        }

        /// Swaps the language live and rebuilds listeners. Persisted.
        public static void SetLanguage(string code)
        {
            PlayerPrefs.SetString("sz_lang", code ?? "en");
            PlayerPrefs.Save();
            _loaded = false;
            _active = null;
            Load();
            Changed?.Invoke();
        }

        public static string NextLanguage()
        {
            string cur = LanguageCode;
            for (int i = 0; i < Languages.Length; i++)
                if (Languages[i].Code == cur)
                    return Languages[(i + 1) % Languages.Length].Code;
            return "en";
        }

        // ---- the files ----
        //   {persistentDataPath}/Loc/sz_loc_ja.json   overrides
        //   {StreamingAssets}/Loc/sz_loc_ja.json      shipped
        // Same shape as sz_tuning.json so JsonUtility reads it directly.
        [System.Serializable] class LocEntry { public string key; public string value; }
        [System.Serializable] class LocFile { public LocEntry[] entries; }

        public static string FileNameFor(string code) => "sz_loc_" + code + ".json";

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string code = LanguageCode;
            if (code == "en") return; // compiled in

            string name = FileNameFor(code);
            string user = System.IO.Path.Combine(
                System.IO.Path.Combine(Application.persistentDataPath, "Loc"), name);
            string shipped = System.IO.Path.Combine(
                System.IO.Path.Combine(Application.streamingAssetsPath, "Loc"), name);

            string path = System.IO.File.Exists(user) ? user
                : System.IO.File.Exists(shipped) ? shipped : null;
            if (path == null)
            {
                Debug.LogWarning($"[SpellyZombie] Loc: no file for '{code}' — "
                    + $"expected {name} in {user} or {shipped}. Showing English.");
                return;
            }

            try
            {
                var f = JsonUtility.FromJson<LocFile>(System.IO.File.ReadAllText(path,
                    System.Text.Encoding.UTF8));
                if (f == null || f.entries == null) return;
                _active = new Dictionary<string, string>();
                foreach (var e in f.entries)
                    if (!string.IsNullOrEmpty(e.key)) _active[e.key] = e.value;
                Debug.Log($"[SpellyZombie] Loc: '{code}' loaded ({_active.Count} strings) from {path}");
            }
            catch (System.Exception ex)
            {
                _active = null;
                Debug.LogError($"[SpellyZombie] Loc: '{code}' file is broken ({ex.Message}) — "
                    + "showing English. Check the JSON commas and quotes.");
            }
        }

        /// Every key with its English text, as a translator-ready JSON file.
        public static string EnglishTemplateJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"entries\": [\n");
            int i = 0;
            foreach (var kv in _en)
            {
                sb.Append("    {\"key\": \"").Append(kv.Key).Append("\", \"value\": \"")
                  .Append(kv.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}");
                if (++i < _en.Count) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        public static int KeyCount => _en.Count;
    }
}
