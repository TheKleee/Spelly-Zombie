# Russian (ru) review sheet

File: `Assets/StreamingAssets/Loc/sz_loc_ru.json`. Register: ты, spoken
imperatives. Buttons use the infinitive (открыть, закрыть), the Russian menu
standard. Cyrillic needs a font with those glyphs: if the hand-drawn skin
font lacks them, fill the OtherFont slot on UISkin.

| key | English | translation | literal back-translation | notes |
|---|---|---|---|---|
| door.open | Open the door | Открыть дверь | to open door | |
| door.close | Close the door | Закрыть дверь | to close door | |
| pickup.weapon | Pick up the weapon | Подобрать оружие | to pick up weapon | |
| pickup.full | Hands full, drop one first | Руки заняты, сначала положи что-нибудь | hands busy, first put something down | |
| chest.try | Try the mystery chest | Открыть загадочный сундук | to open the mysterious chest | "try" became "open", natural Russian |
| perk.drink | Drink {0} | Выпить {0} | to drink {0} | |
| perk.brewed | {0} is already brewed | {0} уже сварено | {0} already brewed | neuter generic, works for any perk name |
| grimoire.open | Open the grimoire | Открыть гримуар | to open grimoire | |
| grimoire.close | Close the grimoire | Закрыть гримуар | to close grimoire | |
| chip.done | Done | Готово | done | |
| carry.down | Put it down | Положить | to put down | |
| scan.aim | Scan it, become it | Просканируй и стань этим | scan and become this | |
| absorb.aim | Absorb it, learn its rune | Поглоти и выучи руну | absorb and learn the rune | |
| chest.open | Open the chest | Открыть сундук | open chest |  |
| chip.grimoire | Grimoire | Гримуар | grimoire | |
| chip.paint | Paint your body | Рисовать на себе | to draw on yourself | |
| chip.first | First person | От первого лица | from the first person | |
| chip.third | Third person | От третьего лица | from the third person | |
| chip.pose | Pose your wizard | Принять позу | to take a pose | "your wizard" dropped |
| chip.watch | Watch your dead | Смотреть за мёртвыми | to watch over the dead | |
| chip.become | Become it again | Снова стать этим | again become this | |
| chip.melt | Melt back to idle | Вернуться в себя | to return into yourself | "melt" and "idle" dropped |
| chip.precise | Faster drawing | Рисовать быстрее | to draw faster | |
| chip.erase | Erase ink | Стереть чернила | to erase ink | |
| chip.absorb | Absorb it | Поглотить | to absorb | |
| hint.alt | Hold {0} to draw faster | Держи {0}, чтобы рисовать быстрее | hold {0}, in order to draw faster | loading hint; {0} = the key bound to precise drawing (ALT) |
| hint.combine | Draw more runes inside of the same seal to combine them | Нарисуй несколько рун внутри одной печати, и они соединятся | draw several runes inside one seal, and they will join | |
| hint.lift | Draw ink on things and press {0} to lift them | Нарисуй чернилами на предмете и нажми {0}, чтобы поднять его | draw with ink on an object and press {0}, in order to lift it | loading hint; {0} = the key or button bound to Use (E) |
| hint.erase | Erasing returns the ink to your wand | Стёртые чернила возвращаются в палочку | erased ink returns into the wand | |
| hint.body | Press {0} to paint runes on your own body | Нажми {0}, чтобы рисовать руны на своём теле | press {0}, in order to draw runes on your own body | loading hint; {0} = the key or button bound to the body key (R) |
| hint.pose | Striking a pose can close a body seal and cast it | Поза может замкнуть печать на теле и сотворить её | a pose can close the seal on the body and cast it | сотворить = cast a spell |
| hint.size | Bigger runes make stronger spells | Чем больше руна, тем сильнее заклинание | the bigger the rune, the stronger the spell | |
| hint.touch | Lines count as one drawing only when they touch | Линии считаются одним рисунком, только если касаются друг друга | lines count as one drawing only if they touch each other | |
| hint.declare | The book can name a drawing that reads wrong | Книга может дать имя рисунку, который прочитан неверно | the book can give a name to a drawing that is read wrongly | |
| hint.trance | Fresh ink puts zombies in a trance | Свежие чернила вводят зомби в транс | fresh ink puts zombies into a trance | |
| hint.wake | Throw a sleeping spell to wake it | Брось спящее заклинание, чтобы разбудить его | throw a sleeping spell, in order to wake it | |
| hint.ghost | The dead rise as ghosts. Fly home to your body and a friend can revive you | Мёртвые становятся призраками. Долети до своего тела, и друг сможет тебя оживить | the dead become ghosts. fly to your body, and a friend will be able to revive you | |
| hint.doors | Doors open when you walk into them | Двери открываются, если в них идти | doors open if you walk into them | |
| paint.done | Done painting | Рисунок готов | drawing ready | |
| paint.pose | Strike a pose | Прими позу | take a pose | |
| paint.orbit | Orbit | Повернуть | to turn | |
| hat.pillar | Pick your hat color | Выбери цвет шляпы | choose the hat's color | |
| side.pillar | Change your side | Сменить сторону | to change side | |
| hat.done | Done | Готово | done | |
| shape.back | Back to yourself | Вернуться в себя | to return into yourself | |
| shape.turn | Turns you | Поворачивает тебя | turns you | |
| shape.save | Saves | Сохраняет | saves | |
| shape.recall | Recalls | Возвращает | brings back | |
| menu.resume | Resume | Продолжить | to continue | |
| menu.restart | Restart run | Начать заново | to begin anew | "run" dropped |
| menu.options | Options | Настройки | settings | |
| menu.share | Share with friends | Поделиться с друзьями | share with friends | pause menu button: puts the game's Steam store link on the clipboard |
| menu.sharecopied | Link copied. Send it to a friend | Ссылка скопирована. Отправь её другу | link copied. send it to-friend | the same button right after the click; the link is ready to paste |
| menu.quit | Quit | Выйти | to exit | |
| menu.back | Back | Назад | back | |
| opt.sens | Look sensitivity: {0} | Чувствительность камеры: {0} | camera sensitivity: {0} | |
| opt.volume | Volume: {0}% | Громкость: {0}% | loudness: {0}% | |
| opt.language | Language: {0} | Язык: {0} | language: {0} | |
| opt.immersive.on | Immersive mode: ON | Режим погружения: ВКЛ | immersion mode: ON | ВКЛ/ВЫКЛ are the standard abbreviations |
| opt.immersive.off | Immersive mode: OFF | Режим погружения: ВЫКЛ | immersion mode: OFF | |
| opt.immersive.hint | No HUD at all. For players who know the game | Никакого интерфейса. Для тех, кто знает игру | no interface at all. for those who know the game | |
| opt.mic | Microphone: {0} | Микрофон: {0} | microphone: {0} | |
| opt.mic.default | Default | По умолчанию | by default | |
| opt.mute | {0}: mute | {0}: заглушить | {0}: to muffle | the word Russian voice apps use for mute |
| opt.unmute | {0}: unmute | {0}: включить звук | {0}: to turn on sound | |
| opt.nobody | Nobody else here to mute | Здесь больше некого глушить | here there is nobody else to mute | |
| menu.leave | Leave lobby | Покинуть лобби | to leave lobby | |
| menu.delete | Delete lobby | Удалить лобби | to delete lobby | |
| menu.play | PLAY | ИГРАТЬ | TO PLAY | |
| lobby.readycall | Ready check. B yes, C no | Все готовы? B да, C нет | all ready? B yes, C no | |
| lobby.ready.on | READY {0}/{1} | ГОТОВЫ {0}/{1} | READY {0}/{1} | plural |
| lobby.ready.off | READY {0}/{1}. B when ready | ГОТОВЫ {0}/{1}. Нажми B, когда будешь готов | READY {0}/{1}. press B when you will be ready | masculine generic готов |
| stand.title | The book stand | Книжная подставка | book stand | |
| stand.hostprivate | Create private lobby (invite only) | Создать закрытое лобби (только по приглашению) | to create a closed lobby (only by invitation) | закрытое = closed, the Russian word for private rooms |
| stand.hostpublic | CREATE PUBLIC LOBBY | СОЗДАТЬ ОТКРЫТОЕ ЛОББИ | TO CREATE OPEN LOBBY | |
| stand.pw | Password (optional) | Пароль (необязательно) | password (not required) | |
| stand.code | Entry code (optional) | Код входа (необязательно) | entry code (not required) | |
| stand.hint | Walk away to close | Отойди, чтобы закрыть | step away, in order to close | |
| stand.map | Change map | Сменить карту | to change map | |
| stand.share | Acolytes at least {0}% | Аколитов не меньше {0}% | of acolytes not less than {0}% | |
| stand.setcode | Set code | Задать код | to set code | |
| stand.readycall | Ready check | Все готовы? | all ready? | |
| stand.invite | Invite friends | Пригласить друзей | to invite friends | |
| stand.start | START | СТАРТ | START | |
| stand.waiting | Waiting for ready | Ждём готовности | we wait for readiness | |
| stand.delete | Delete lobby | Удалить лобби | to delete lobby | |
| stand.kick | Kick | Выгнать | to drive out | |
| stand.ban | Ban | Забанить | to ban | gamer word |
| stand.banned | Banned: {0} | В бане: {0} | in the ban: {0} | |
| stand.unban | Unban | Разбанить | to unban | |
| stand.name | Lobby name | Название лобби | name of the lobby | |
| stand.size | Size {0} | Мест {0} | seats {0} | |
| stand.region | Region: {0} | Регион: {0} | region: {0} | |
| stand.tab.host | HOST | СОЗДАТЬ | CREATE | |
| stand.tab.join | JOIN | ВОЙТИ | ENTER | |
| stand.settings | Settings | Настройки | settings | |
| stand.regions | Regions | Регионы | regions | |
| stand.langs | Languages | Языки | languages | |
| stand.behaviors | Behaviors | Настроение | mood | the tags under it are moods |
| stand.duration | Time {0} min | Время {0} мин | time {0} min | |
| stand.setpw | Set password | Задать пароль | to set password | |
| stand.hosting | HOSTING your lobby | ТЫ ведёшь это лобби | YOU lead this lobby | |
| stand.players | Players | Игроки | players | |
| filter.all | All | Все | all | |
| browse.refresh | Refresh | Обновить | to refresh | |
| browse.join | JOIN | ВОЙТИ | ENTER | |
| browse.locked | (Password) | (Пароль) | (password) | |
| browse.none | No lobbies found. Host one! | Лобби не найдено. Создай своё! | lobby not found. create your own! | |
| browse.needpw | This lobby wants a password | Это лобби просит пароль | this lobby asks for a password | |
| browse.cancel | Cancel | Отмена | cancel | |
| region. | Any region | Любой регион | any region | |
| region.eu | Europe | Европа | Europe | |
| region.na | North America | Северная Америка | North America | |
| region.sa | South America | Южная Америка | South America | |
| region.asia | Asia | Азия | Asia | |
| region.oce | Oceania | Океания | Oceania | |
| region.mea | Middle East & Africa | Ближний Восток и Африка | Near East and Africa | Ближний Восток is the Russian name for the Middle East |
| tag.welcome | Everyone welcome | Всем рады | glad to see everyone | the fixed Russian phrase for "everyone welcome" |
| tag.beginners | Beginners welcome | Новичкам рады | glad to see newbies | |
| tag.casual | Casual fun | Расслабленно | relaxed | |
| tag.tryhard | Try hards | Всерьёз | in earnest | |
| tag.mic | Mic on | С микрофоном | with microphone | |
| tag.quiet | Quiet ok | Можно молча | silently is allowed | |
| opt.mic.title | Microphone | Микрофон | microphone |  |
| opt.mic.open | Open mic | Всегда включён | always switched on |  |
| opt.mic.ptt | Hold {0} | Держи {0} | hold {0} | Options, Audio, microphone mode button: push to talk; {0} = the key bound to talking (V) |
| opt.mic.off | Muted | Выключен | switched off |  |
| opt.tab.game | Game | Игра | game |  |
| opt.tab.video | Video | Видео | video |  |
| opt.tab.audio | Audio | Звук | sound |  |
| opt.resolution | Resolution: {0} | Разрешение: {0} | resolution: {0} |  |
| opt.resolution.title | Resolution | Разрешение | resolution |  |
| opt.display | Display | Экран | screen |  |
| opt.display.full | Fullscreen | Полный экран | full screen |  |
| opt.display.borderless | Borderless | Без рамки | without frame |  |
| opt.display.windowed | Windowed | В окне | in a window |  |
| opt.quality | Quality | Качество | quality |  |
| opt.low | Low | Низкое | low |  |
| opt.medium | Medium | Среднее | medium |  |
| opt.high | High | Высокое | high |  |
| opt.textures | Textures | Текстуры | textures |  |
| opt.shadows | Shadows | Тени | shadows |  |
| opt.effects | Effects | Эффекты | effects |  |
| opt.motionblur | Motion blur | Размытие в движении | blur in motion |  |
| opt.aa | Antialiasing | Сглаживание | smoothing | the Russian term |
| opt.fps | Frame limit | Лимит кадров | frame limit |  |
| opt.vsync | VSync | Вертикальная синхронизация | vertical synchronisation | long, it is a row label not a button |
| opt.off | Off | Выкл | off |  |
| opt.on | On | Вкл | on |  |
| opt.music | Music: {0}% | Музыка: {0}% | music: {0}% |  |
| opt.sfx | Sounds: {0}% | Звуки: {0}% | sounds: {0}% |  |
| seal.norune | No rune here to seal. Aim at one of your runes | Здесь нет руны для печати. Целься в одну из своих рун | here there is no rune for a seal. aim at one of your runes |  |
| seal.noink | Not enough ink for the seal | Не хватает чернил на печать | ink is not enough for the seal |  |
| rune.noink | Not enough ink to finish the rune | Не хватает чернил, чтобы дорисовать руну | ink is not enough to finish drawing the rune |  |
| round.safe | THE LOBBY IS SAFE GROUND | В ЛОББИ БЕЗОПАСНО | in the lobby it is safe | no dash, the game's voice |
| round.versus | WIZARDS vs ACOLYTES | МАГИ против АКОЛИТОВ | wizards against acolytes |  |
| round.wizards | WIZARDS WIN | МАГИ ПОБЕДИЛИ | wizards won |  |
| round.acolytes | ACOLYTES WIN | АКОЛИТЫ ПОБЕДИЛИ | acolytes won |  |
| round.home | {0}. Back to the lobby in {1} | {0}. В лобби через {1} | {0}. to lobby in {1} | {1} is seconds |
| round.pot |  · Pot {0}% |  · Котёл {0}% | cauldron {0}% |  |
| round.green |  · The pot is GREEN |  · Котёл ЗЕЛЁНЫЙ | cauldron GREEN |  |
| gate.accepts | THE GATE ACCEPTS | ВРАТА ПРИНЯЛИ | the gate accepted | врата, the old word for a magic gate |
| net.hostleft | THE HOST LEFT | ХОСТ УШЁЛ | host left |  |
| net.hosting | ● HOSTING, {0} player(s) | ● ТЫ ХОСТ, игроков: {0} | you host, players: {0} | count after the noun sidesteps Russian plurals |
| net.connected | ● CONNECTED, {0} player(s) | ● ПОДКЛЮЧЕНО, игроков: {0} | connected, players: {0} |  |
| net.map | MAP: {0} | КАРТА: {0} | map: {0} |  |
| net.maplikes | MAP: {0} · ♥{1} | КАРТА: {0} · ♥{1} | map: {0} · ♥{1} |  |
| steam.offline | Steam not running, offline & LAN only | Steam не запущен, только офлайн и LAN | Steam not launched, only offline and LAN |  |
| steam.ready | Steam ready: {0} | Steam готов: {0} | Steam ready: {0} | {0} is the Steam name |
| steam.leavefirst | Leave your lobby first | Сначала выйди из своего лобби | first exit from your lobby |  |
| steam.ping | Your ping to that host is {0}ms, lobby allows {1} | Твой пинг до этого хоста {0} мс, лобби разрешает {1} | your ping to this host {0} ms, lobby allows {1} |  |
| steam.joining | Joining… | Заходим… | we enter… |  |
| steam.deleted | Lobby deleted | Лобби удалено | lobby deleted |  |
| steam.notrunning | Steam not running | Steam не запущен | Steam not launched |  |
| steam.creating | Creating lobby… | Создаём лобби… | we create lobby… |  |
| steam.failed | Lobby failed: {0} | Лобби не создано: {0} | lobby not created: {0} | {0} is a Steam error code |
| steam.noenter | Couldn't enter the lobby | Не удалось войти в лобби | not managed to enter into lobby |  |
| steam.nohost | Lobby has no host, try again | У лобби нет хоста, попробуй ещё раз | at lobby there is no host, try once more |  |
| steam.connecting | Joined, connecting… | Вошли, подключаемся… | entered, we connect… |  |
| steam.private | PRIVATE LOBBY, invite friends | ЗАКРЫТОЕ ЛОББИ, зови друзей | closed lobby, call friends |  |
| steam.public | PUBLIC LOBBY, listed | ОТКРЫТОЕ ЛОББИ, в списке | open lobby, in list |  |
| menu.tagline | Draw fast. Die funny. | Рисуй быстро. Умирай смешно. | draw fast. die funnily. |  |
| menu.close | Close | Закрыть | to close | infinitive, the Russian button standard |
| chip.pages | Turn the pages | Листать страницы | to leaf pages | the mouse wheel turns the grimoire pages |
| round.potopens |  · Pot opens in {0} |  · Котёл откроется через {0} | cauldron opens in {0} | seconds; banner word while the pot brews / while the ink hops to the next pot |
| round.inkflight |  · Ink in flight {0} |  · Чернила в полёте {0} | ink in flight {0} | seconds; banner word while the pot brews / while the ink hops to the next pot |
| opt.uiscale | UI size: {0}% | Размер интерфейса: {0}% | interface size: {0}% | options, game tab: the slider that scales every panel and chip |
| stand.nocap | No cap | Без ограничения | no limit | book stand, the size row at Steam's ceiling of 250: the host set no cap |
| stand.heavy | Your connection carries everyone | Твоё соединение тянет всех | your connection pulls everyone along | book stand, under the size row past 32 players: the host's upload carries the lobby |
| menu.quit.game | Quit the game? | Выйти из игры? | exit from the game? | the Quit check on the main menu: Quit closes the game |
| menu.quit.lobby | Back to the main menu? | Выйти в главное меню? | exit into the main menu? | the Quit check in the lobby: Quit goes to the main menu. Uses the red button's verb, so the red button reads as the yes |
| menu.quit.match | Leave the match for an empty lobby? | Покинуть матч? Окажешься в пустом лобби. | leave the match? (you) end up in an empty lobby. | the Quit check on a map: Quit leaves the match for your own empty lobby |
| menu.quit.host | You are the host. Everyone else goes home too. | Ты хост. Все остальные тоже уйдут домой. | you are the host. all the others will also go home. | under the Quit check when you host other players: they are sent back to their own lobby |
| menu.cancel | Cancel | Отмена | cancellation | the grey button of the Quit check. Cancel, not Back: under 'Back to the main menu?' a Back button read like the yes |
| menu.mapcreator | Map Creator | Редактор карт | editor of-maps | the main menu button that opens the Map Creator |
| creator.help | Right mouse looks, middle mouse drags the view, {0} flies, Space rises and Ctrl sinks, the wheel moves forward and back. Left click places the picked piece or picks one up to drag it. Delete removes, {1} turns, {2} {3} resize, Ctrl+Z undoes and Ctrl+R redoes. | Правая кнопка мыши: осмотреться. Средняя кнопка: тащить вид. {0}: полёт. Пробел: вверх, Ctrl: вниз, колесо: вперёд и назад. Левый клик: поставить выбранную деталь или схватить поставленную и тащить. Delete: удалить. {1}: повернуть. {2} {3}: размер. Ctrl+Z: отменить, Ctrl+R: вернуть. | right button of-mouse: look around. middle button: drag view. {0}: flight. space: up, Ctrl: down, wheel: forward and back. left click: place chosen piece or grab placed-one and drag. Delete: remove. {1}: turn. {2} {3}: size. {2} {3}: size. Ctrl+Z: cancel, Ctrl+R: bring back | the controls line in the Map window; {0} the move keys, {1} turn, {2} {3} size, as printed on this keyboard |
| creator.timer | Match clock: {0} min | Таймер матча: {0} мин | timer of-match: {0} min | the match clock the map wants; {0} minutes |
| creator.noclock | Match clock: none | Таймер матча: нет | timer of-match: none | no clock, the match runs until a team wins |
| creator.place | PLACE | ПОСТАВИТЬ | PLACE | heading over the list of pieces |
| creator.pickone | Pick a piece below, then click the ground | Выбери деталь ниже и кликни по земле | choose a piece below and click on the ground | shown before any piece is picked |
| creator.body | Body: {0} | Тело: {0} | body: {0} | a creature body from the Spell Crafter, by its name |
| creator.rotate | Turn | Повернуть | turn | turns the selected piece a step |
| creator.bigger | Bigger | Больше | bigger | scales the selected piece up |
| creator.smaller | Smaller | Меньше | smaller | scales the selected piece down |
| creator.delete | Delete | Удалить | delete | removes the selected piece |
| creator.save | Save | Сохранить | save | saves the map |
| creator.new | New map | Новая карта | new map | starts an empty map |
| creator.load | SAVED MAPS | СОХРАНЁННЫЕ КАРТЫ | SAVED MAPS | heading over the saved maps list |
| creator.saved | Saved {0} | Карта сохранена: {0} | map saved: {0} | confirmation after a save; {0} the map name |
| maps.title | Maps | Карты | maps | title of the Maps screen the main menu's Map Creator button opens |
| maps.tab.shared | Shared maps | Общие карты | shared maps | tab: maps other players shared on the Workshop |
| maps.tab.mine | My maps | Мои карты | my maps | tab: the maps saved on this computer |
| maps.create | New blank map | Новая пустая карта | new empty map | button: opens the creator on an empty map with only the sky |
| maps.back | Back | Назад | back | leaves the Maps screen or the preview |
| maps.pick | Pick a map to see it here | Выбери карту, чтобы увидеть её здесь | choose map to see it here | the empty detail pane |
| maps.preview | Preview | Посмотреть | take a look | opens a map to fly around it without editing |
| maps.keep | Save | Сохранить | save | keeps a shared map: it joins My maps and the lobby list |
| maps.kept | Saved | Сохранена | saved (the map) | the same button once the map is in My maps |
| maps.edit | Edit | Изменить | change | opens your map in the creator |
| maps.share | Share | Поделиться | share | uploads your map to the Workshop, or updates it |
| maps.page | Open its Steam page | Открыть страницу в Steam | open page in Steam | the shared map's Workshop page in the Steam overlay |
| maps.delete.sure | Delete for good? | Удалить навсегда? | delete forever? | the second click on Delete, for your own map |
| maps.by | By {0} | Автор: {0} | author: {0} | the map's author; {0} a Steam name |
| maps.info | {0} biomes, {1} creatures, {2} pieces | Биомов: {0}, существ: {1}, деталей: {2} | of-biomes: {0}, of-creatures: {1}, of-pieces: {2} | what a saved map holds |
| maps.none | Nobody has shared a map yet | Пока никто не поделился картой | so-far nobody not shared map | Shared maps is empty |
| maps.nosteam | Shared maps need Steam | Для общих карт нужен Steam | for shared maps needed Steam | Shared maps while Steam is not running |
| maps.loading | Looking for shared maps | Ищем общие карты | we-search shared maps | while the Workshop answers |
| maps.downloading | Downloading the map | Загружаем карту | we-load map | while Steam downloads a shared map |
| maps.sharing | Sharing your map | Делимся твоей картой | we-share your map | while the upload runs |
| maps.done.shared | Your map is shared | Твоя карта опубликована | your map published | the upload finished |
| maps.failed | That did not work: {0} | Не получилось: {0} | not worked-out: {0} | a Workshop step failed; {0} is Steam's reason word |
| maps.description | A Spelly Zombie map with {0} biomes, made by {1} | Карта Spelly Zombie: биомов {0}, автор {1} | map Spelly Zombie: of-biomes {0}, author {1} | the Workshop item's description; {0} a count, {1} the author |
| mc.map | Map | Карта | map | creator window and ribbon button: name, ground, clock, save |
| mc.biomes | Biomes | Биомы | biomes | creator window and ribbon button: the biome palette |
| mc.biome | Biome | Биом | biome | the window that edits the selected biome |
| mc.objects | Objects | Объекты | objects | creator window and ribbon button: pieces and creatures to place |
| mc.spells | Spells | Заклинания | spells | creator window and ribbon button: the map's spellbook |
| mc.runes | Runes | Руны | runes | creator window and ribbon button: the map's runes |
| mc.generate | Generate | Создать | create | grows the ground from the biome boxes |
| mc.hint.idle | Pick a biome or an object, then click the map | Выбери биом или объект и кликни по карте | choose biome or object and click on map | the line under the ribbon with nothing picked |
| mc.hint.place | Click the map to place {0}. Esc stops | Кликни по карте, чтобы поставить {0}. Esc отменяет | click on map to place {0}. Esc cancels | a biome is picked; {0} its name |
| mc.hint.placemany | Click the map to place {0} as many times as you like. Esc stops | Кликай по карте, чтобы ставить {0} сколько хочешь. Esc отменяет | keep-clicking on map to place {0} as-much-as you-want. Esc cancels | an object is picked; {0} its name; every click places one |
| mc.hint.biome | Drag {0} to move it, the gold balls size it, the tall handle lifts and lowers it | Тащи {0}, чтобы сдвинуть, золотые шарики меняют размер, высокая ручка поднимает и опускает | drag {0} to move, golden little-balls change size, tall handle raises and lowers | a biome is selected; {0} its name |
| mc.hint.object | Drag it to move it, {0} turns it, {1} {2} size it, Delete removes it | Тащи: сдвинуть. {0}: повернуть. {1} {2}: размер. Delete: удалить | drag: move. {0}: turn. {1} {2}: size. Delete: remove | a placed piece or creature is selected; {0} {1} {2} are this keyboard's turn and size keys; Delete named like in creator.help |
| mc.hint.stale | The ground is out of date: press Generate | Земля устарела: нажми «Создать» | ground became-outdated: press 'create' | boxes changed since the ground last grew; names the Generate button |
| mc.hint.preview | Right mouse looks around, {0} flies | Правая кнопка мыши: осмотреться. {0}: полёт | right button of-mouse: look around. {0}: flight | the line in preview; {0} the move keys of this keyboard |
| mc.name | Name | Название | name | the name field of a map, biome, spell or rune |
| mc.groundhead | Ground | Земля | ground | heading over growing the ground, and over a biome's ground settings |
| mc.newseed | Another ground | Другая земля | other ground | grows the same biomes with a new random seed |
| mc.frombase | Start from the island's biomes | Начать с биомов острова | start with biomes of-island | copies every biome of Spelly Island onto an empty map |
| mc.picture | Saving takes the map's picture from where the camera looks | При сохранении картинкой карты станет вид с камеры | at saving picture of-map will-become view from camera | note over Save: the card picture is a camera shot |
| mc.biomes.note | Pick a biome, then click the map to place it | Выбери биом и кликни по карте, чтобы поставить его | choose biome and click on map to place it | note in the Biomes window |
| mc.onmap | On this map: {0} | На этой карте: {0} | on this map: {0} | heading over the list of placed biomes; {0} how many |
| mc.kind.ground | Ground | Земля | ground | switch in the Biome window: a ground biome |
| mc.kind.liquid | Liquid | Жидкость | liquid | switch in the Biome window: a liquid biome (water, magma); also the heading over its liquid settings |
| mc.shape | Box | Коробка | box | heading over the biome box's size, height, layer and safe middle |
| mc.width | Width | Ширина | width | biome box size, left to right, metres |
| mc.depth | Length | Длина | length | biome box size, front to back, metres |
| mc.height | Height | Высота | height | biome box height, metres |
| mc.bottom | Bottom height | Высота дна | height of-bottom | where the biome box starts, metres above or below zero |
| mc.layer | Layer | Слой | layer | a higher layer cuts through lower biomes where boxes overlap |
| mc.core | Middle nobody can cut | Середина, которую не срезать | middle which not to-cut | 0 to 1: how much of the box's middle no higher layer may cut |
| mc.rough | Bumpiness | Неровность | unevenness | how far the ground wanders up and down; 0 = flat |
| mc.hills | Hill size | Размер холмов | size of-hills | metres per bump: bigger = wider, calmer hills |
| mc.paths | Paths through it | Тропы через него | paths through it | on = the path network may run through this biome |
| mc.pathbend | How much paths bend | Извилистость троп | winding of-paths | 0 = dead straight streets, 1 = a wandering trail |
| mc.brush | Ground look | Вид земли | look of-ground | picks the terrain paint for the ground |
| mc.pathbrush | Path look | Вид троп | look of-paths | picks the terrain paint for paths |
| mc.none | None | Нет | none | a pick left empty (no path look, no cauldron, no area...) |
| mc.fill | Things inside | Что внутри | what inside | heading over what the biome scatters inside itself |
| mc.spacing | Room for each thing, metres | Места на вещь, метры | of-room per thing, metres | grid cell size: small for a peak, house-sized for a town |
| mc.slope | Steepest ground, degrees | Самый крутой склон, градусы | most steep slope, degrees | things never land on ground steeper than this |
| mc.props | Things scattered here: {0} | Разбросанные вещи: {0} | scattered things: {0} | folded list of the prefabs this biome scatters; {0} how many are picked |
| mc.sources | Glowing sources: {0} | Светящиеся источники: {0} | glowing sources: {0} | folded list of the glowing things wizards absorb runes from; {0} how many are picked |
| mc.minsources | Fewest glowing sources | Меньше всего источников | fewest of-all sources | at least this many glowing sources appear here |
| mc.maxsources | Most glowing sources | Больше всего источников | most of-all sources | never more than this many glowing sources here |
| mc.walllight | Light on house walls | Свет на стенах домов | light on walls of-houses | a lamp hung on an outer wall of every house here |
| mc.cauldron | Cauldron | Котёл | cauldron | the pot this biome may host; same noun as the pot in the match HUD |
| mc.landmark | Landmark | Ориентир | landmark | the centerpiece that stands in the biome's middle |
| mc.imposed | Forced on everything here | Навязано всему здесь | imposed on-everything here | heading: these six win over whatever a thing naturally is (a 60 degree place makes you 60) |
| mc.allowed | Limits here | Пределы здесь | limits here | heading: nothing here gets more than these (courage is pushed both ways) |
| mc.healing | Healing speed | Скорость лечения | speed of-healing | multiplies how fast strength mends here; 1 = natural |
| mc.spawn | Wizard start | Старт магов | start of-wizards | heading over where the wizards begin a match |
| mc.wizardhome | Wizards start here | Маги начинают здесь | wizards start here | switch: the wizards begin the match in this biome |
| mc.spread | How spread out | Разброс | spread | how far from the middle the wizards may start |
| mc.byobjects | Only around its things | Только вокруг своих вещей | only around own things | heading + switch: on = the biome lives only around the things placed in it; break them all and it is gone |
| mc.full | Full within, metres | Полностью до, метры | fully up-to, metres | the effect is full this close to one of its things |
| mc.reach | Gone past, metres | Исчезает после, метры | disappears after, metres | the effect has faded to nothing this far from its things |
| mc.buoyancy | Floatiness | Плавучесть | buoyancy | 1 = you float on top, just under 1 = water's slow sink, 0 = no lift |
| mc.surface | Surface | Поверхность | surface | the liquid's top sheet (water, magma) |
| mc.duplicate | Copy | Копировать | copy | makes a copy of the selected biome next to it |
| mc.team | Fights for | На чьей стороне | on whose side | a placed creature's side |
| mc.team.wild | Nobody | Ни на чьей | on nobody's | wild: hunts everyone |
| mc.team.wizards | Wizards | Магов | of-wizards (on the wizards' side) | the wizards' side |
| mc.team.acolytes | Acolytes | Аколитов | of-acolytes (on the acolytes' side) | the acolytes' side |
| mc.size | Size | Размер | size | a placed creature's size; 1 = the body's own |
| mc.delay | First one after, seconds | Первый через, секунды | first after, seconds | seconds after the match starts before the first appears |
| mc.every | Again every, seconds (0 = once) | Снова каждые, секунды (0 = один раз) | again every, seconds (0 = one time) | a new wave every this many seconds; 0 = only once |
| mc.count | How many each time | Сколько за раз | how-many per time | creatures per wave |
| mc.maxalive | Most alive at once (0 = no limit) | Больше всего живых сразу (0 = без предела) | most of-all alive at-once (0 = without limit) | never more of its own alive than this |
| mc.forever | Lives until killed | Живёт, пока не убьют | lives until not they-kill | off = it fades like a summon (a golem after 30 s) |
| mc.spells.note | These spells belong to this map. The main game keeps its own | Эти заклинания принадлежат этой карте. В основной игре остаются свои | these spells belong to-this map. in main game remain its-own | note at the top of the Spells window |
| mc.spell.new | New spell | Новое заклинание | new spell | button, and the name a new spell starts with |
| mc.body.zombie | Zombie | Зомби | zombie | a spell that is a zombie body |
| mc.body.golem | Golem | Голем | golem | a spell that is a golem body |
| mc.spell.book | Book | Гримуар | grimoire | whose grimoire the spell belongs to: Wizard or Acolyte |
| mc.side.wizard | Wizard | Маг | wizard | the wizards' grimoire, or the wizard side's emoji |
| mc.side.acolyte | Acolyte | Аколит | acolyte | the acolytes' grimoire, or the acolyte side's emoji |
| mc.summonedby | Summoned by | Призывают руны | summon runes (runes summon it) | heading: the runes a seal must hold to raise this body; click one to take it off |
| mc.summon.none | Nothing summons this yet | Пока ничто его не призывает | so-far nothing it not summons | no rune added yet |
| mc.bornas | Born as | Рождается таким | is-born such | heading: the body's natural numbers, where it drifts from |
| mc.cando | Can do | Умеет | is-able | heading: its charge and the spells it casts |
| mc.charge | Charge | Рывок | dash | the body rushes at its target; the one move a body has |
| mc.casts | Casts | Колдует | casts spells | over the spells of its own book it can cast; lit = it casts it |
| mc.conditions | Conditions | Условия | conditions | heading: what must be true for a particle spell to exist |
| mc.places | Lock as a biome | Закрепить как биом | fix as biome | over six toggles: a locked condition stays put like a biome; only another spell moves it |
| mc.effects | Effects | Эффекты | effects | heading: byproducts that land on whatever the spell touches |
| mc.onlyliving | Only things with a mind | Только то, у чего есть разум | only that which has mind | on = the spell picks its victims: only things that have a mind |
| mc.area | Area | Область | area | heading: the area effect that rides on this spell |
| mc.spellshape | Shape | Форма | shape | heading: the saved shape the spell wears |
| mc.material | Material | Материал | material | folded sliders for how the spell's surface moves; everything starts still |
| mc.look.wobble | Liquid wobble | Колыхание жидкости | swaying of-liquid | material slider |
| mc.look.wobblespeed | Liquid speed | Скорость жидкости | speed of-liquid | material slider: how fast the wobble moves |
| mc.look.swirl | Gas swirl | Завихрение газа | swirling of-gas | material slider |
| mc.look.swirlspeed | Swirl speed | Скорость завихрения | speed of-swirling | material slider |
| mc.look.turbulence | Turbulence | Турбулентность | turbulence | material slider |
| mc.look.bubbles | Bubbles | Пузыри | bubbles | material slider |
| mc.look.bubblesize | Bubble size | Размер пузырей | size of-bubbles | material slider |
| mc.look.bubblerise | Bubble rise | Подъём пузырей | rise of-bubbles | material slider: how fast bubbles climb |
| mc.look.holes | Break-up | Распад | disintegration | material slider: holes open in the surface |
| mc.look.holesize | Hole size | Размер дыр | size of-holes | material slider |
| mc.look.rim | Rim glow | Свечение края | glowing of-edge | material slider: light around the edge |
| axis.0 | Temperature | Температура | temperature | the Spell Crafter's axis 0, chill to hot, in degrees; also a biome's heat |
| axis.1 | Light | Свет | light | axis 1, dark to bright, % |
| axis.2 | Pressure | Давление | pressure | axis 2, spread out to compressed, %; a biome's density |
| axis.3 | Balance | Равновесие | balance | axis 3, slick to sticky, %: how planted things are |
| axis.4 | State | Состояние | state | axis 4, gas below -50, liquid, solid above +50 |
| axis.5 | Affinity | Сродство | affinity | axis 5, repels to attracts, % |
| axis.6 | Strength | Сила | strength | axis 6, frail to strong, hp: strength is health |
| axis.7 | Mind | Разум | mind | axis 7, mindless to clever, % |
| axis.8 | Courage | Храбрость | bravery | axis 8, afraid to fearless, % |
| axis.9 | Clones | Клоны | clones | axis 9, alone to many: whole copies of itself |
| mc.runes.note | These runes belong to this map. Drawings you keep teach the map to read them | Эти руны принадлежат этой карте. Рисунки, которые ты оставишь, научат её их читать | these runes belong to-this map. drawings which you will-keep will-teach it them to-read | note at the top of the Runes window |
| mc.rune.new | New rune | Новая руна | new rune | button, and the name a new rune starts with |
| mc.emoji | Emoji | Эмодзи | emoji | heading over the rune's emoji, one per side |
| mc.clear | Clear | Очистить | clear | removes the emoji, or wipes the drawing pad |
| mc.pushes | Pushes | Толкает | pushes | heading: what drawing this rune adds to the seal's numbers |
| mc.drawing | Drawing | Рисунок | drawing | heading over the drawing pad |
| mc.pad.note | Left mouse draws, right mouse erases | Левая кнопка мыши рисует, правая стирает | left button of-mouse draws, right erases | under the Drawing heading |
| mc.undo | Undo | Отменить | cancel | drawing pad |
| mc.redo | Redo | Вернуть | bring back | drawing pad |
| mc.keep | Keep this drawing | Оставить этот рисунок | keep this drawing | the pad's drawing becomes one of the rune's samples for this map |
| mc.kept | Kept drawings. Pick one to work on it, the plus starts a new one, X removes one | Оставленные рисунки. Выбери рисунок, чтобы править его, плюс начинает новый, X убирает | kept drawings. choose drawing to edit it, plus starts new, X removes | heading over the drawing tiles of this rune on this map |
| mc.reads | Reads as {0} | Читается как {0} | is-read as {0} | what the pad's drawing is read as right now; {0} a rune name |
| mc.reads.none | Not readable yet | Пока не читается | so-far not is-read | the pad's drawing matches no rune well enough |
| browse.inlobby | In the lobby | В лобби | in the lobby | lobby list row, after the tags: the host and friends are in the lobby scene (the sandbox), no match running |
| browse.inmatch | In a match | В матче | in a match | lobby list row, after the tags: a match is running on a map right now |
| browse.upmin | Up {0} min | Открыто {0} мин | open {0} min | lobby list row: how long the lobby has stood; {0} = whole minutes, under an hour |
| browse.uph | Up {0} h {1} min | Открыто {0} ч {1} мин | open {0} h {1} min | lobby list row: how long the lobby has stood; {0} = hours, {1} = the minutes past the hour |
| hint.sealshape | A circle is the weakest seal, a triangle the strongest | Круглая печать слабее всех, треугольная сильнее всех | a round seal is weaker than all, a triangular one stronger than all | loading screen hint; Spell.PowerFor: fewer lines in a seal = stronger, a circle counts as ten |
| hint.sealline | Draw two sides of a triangle around your runes and {0} closes it | Нарисуй две стороны треугольника вокруг своих рун, и {0} замкнёт его | draw two sides of a triangle around your runes, and {0} will close it | loading screen hint; {0} = the key bound to F; the book closes two straight lines that meet at a corner with the third side |
| opt.ghost | Ghost lines | Призрачные линии | ghost lines | Game tab of the options: the row for the ghost hand (the lobby lessons and the floating F's drawn previews), answered Off / On |
| side.pillar.acolyte | Become an acolyte | Стать аколитом | to become an acolyte | caption under the floating E on the lobby's side pillar while you are a wizard; same style as chest.open |
| side.pillar.wizard | Become a wizard | Стать магом | to become a wizard | caption under the floating E on the lobby's side pillar while you are an acolyte |
| seal.aim | Seal it, the book draws the ring | Запечатай, книга нарисует кольцо | seal it, the book will draw the ring | caption under the floating F on your own rune while the grimoire is open on the seal page; same style as scan.aim and absorb.aim |
| mc.area.arrive | Arrives in, seconds | Долетает за, секунд | flies there in, seconds | area editor, under Where it starts: how long the area takes from its start point to the spell |
| mc.note.arrive | 0 lets the game pick the speed. Slower than 5 metres a second it settles in, faster it slams. | При 0 скорость выбирает игра. Медленнее 5 метров в секунду оно мягко садится, быстрее врезается | at 0 the game chooses the speed. slower than 5 metres per second it lands softly, faster it crashes in | area editor: small note under Arrives in |
| opt.tab.keys | Controls | Управление | controls | fourth tab of the Options, after Game, Video, Audio: the key and controller button list |
| keys.note | Click a key or a button, then press the new one. Escape keeps the old one | Нажми на клавишу или кнопку, потом нажми новую. Esc оставит старую | click on a key or a button, then press the new one. Esc will leave the old one | Controls tab: small note above the list |
| keys.press | Press the new key or button now. Escape keeps the old one | Нажми новую клавишу или кнопку. Esc оставит старую | press the new key or button. Esc will leave the old one | Controls tab: the note while a binding waits for its new key |
| keys.action | Action | Действие | action | Controls tab: heading of the first column |
| keys.keyboard | Keyboard | Клавиатура | keyboard | Controls tab: heading of the keyboard and mouse column |
| keys.pad | Controller | Геймпад | gamepad | Controls tab: heading of the gamepad column |
| keys.reset | Back to the default keys | Вернуть клавиши по умолчанию | return the keys by default | Controls tab: button that puts every key and button back as shipped |
| keys.act.forward | Walk forward | Идти вперёд | go forward | Controls tab: an action's name |
| keys.act.back | Walk back | Идти назад | go back | Controls tab: an action's name |
| keys.act.left | Walk left | Идти влево | go left | Controls tab: an action's name |
| keys.act.right | Walk right | Идти вправо | go right | Controls tab: an action's name |
| keys.act.jump | Jump, fly up | Прыжок, лететь вверх | jump, fly up | Controls tab: jumping, and rising as a ghost |
| keys.act.crouch | Crouch, fly down | Присесть, лететь вниз | crouch, fly down | Controls tab: crouching, and sinking as a ghost |
| keys.act.sprint | Run | Бежать | run | Controls tab: sprinting |
| keys.act.draw | Draw | Рисовать | draw | Controls tab: drawing ink |
| keys.act.erase | Erase | Стирать | erase | Controls tab: erasing ink |
| keys.act.precise | Draw faster | Рисовать быстрее | draw faster | Controls tab: the hold key that frees the cursor for faster drawing |
| keys.act.use | Grab, throw, use | Взять, бросить, использовать | take, throw, use | Controls tab: the one hand key (E) |
| keys.act.drop | Drop, let go | Бросить, отпустить | drop, release | Controls tab: the release key (F) |
| keys.act.body | Paint body, pose, watch | Рисовать на теле, поза, смотреть | draw on the body, pose, watch | Controls tab: the body key (R): wizards paint their body, third person poses, acolytes watch their zombies |
| keys.act.menu | Menu | Меню | menu | Controls tab: the pause menu key |
| keys.act.ready | Ready | Готов | ready | Controls tab: answering the lobby's ready check with yes |
| keys.act.notready | Not ready | Не готов | not ready | Controls tab: answering the lobby's ready check with no |
| keys.act.talk | Push to talk | Нажми, чтобы говорить | press to talk | Controls tab: holding this opens the microphone in push to talk mode |
| keys.act.prev | Previous page | Предыдущая страница | previous page | Controls tab: a controller button that turns the grimoire back a page, or steps to the previous pose or zombie |
| keys.act.next | Next page | Следующая страница | next page | Controls tab: a controller button that turns the grimoire forward a page, or steps to the next pose or zombie |
| watch.many | Watching {0} of {1} · 1-0 picks · Draw a seal on it to blow it | Смотришь {0} из {1} · 1-0 выбирает · Нарисуй на нём печать, чтобы взорвать | you watch {0} of {1} · 1-0 chooses · draw a seal on it to blow it up | prompt bar while an acolyte looks through one of several zombies of theirs: {0} = which one, {1} = how many; the number keys 1 to 0 pick another; a seal drawn on the zombie makes it explode |
| watch.one | Draw a seal on it to blow it · R leaves | Нарисуй на нём печать, чтобы взорвать · R выходит | draw a seal on it to blow it up · R exits | prompt bar while an acolyte looks through their only zombie |
| mc.behaviour.rampages | Rampages | Буйствует | rampages | creature behaviour button, after Skittish: a calamity that throws its spells everywhere |
| mc.note.rampages | Throws random spells from its list all over the place, about one a second, and never runs away. Spells from its own side cannot hurt it. | Швыряет случайные заклинания из своего списка куда попало, примерно одно в секунду, и никогда не убегает. Заклинания своей стороны ему не вредят. | hurls random spells from its list wherever, about one a second, and never runs away. spells of its own side do not harm it | note under the behaviour buttons when Rampages is picked |
| opt.keys.title | Videos and photos | Видео и фото | video and photo | pause menu, Game tab: heading over the three recorder keys |
| opt.key.clip | Start or stop a video | Начать или остановить видео | start or stop a video | pause menu, Game tab: beside the I keycap; the same key starts and stops the recording |
| opt.key.photo | Take a photo | Сделать фото | make a photo | pause menu, Game tab: beside the P keycap |
| opt.key.folder | Open their folder (in the lobby) | Открыть их папку (в лобби) | open their folder (in the lobby) | pause menu, Game tab: beside the O keycap; opens the folder holding the saved videos and photos, and only works in the lobby |
| opt.keys.note | Saved in your Videos folder, under Spelly Zombie. The interface is never in them | Сохраняются в папке «Видео», в Spelly Zombie. Интерфейс в них никогда не попадает | they are saved in the 'Video' folder, in Spelly Zombie. the interface never gets into them | pause menu, Game tab: small note under the three recorder keys |
| ver.mine | That lobby runs a newer version. Quit the game and let Steam update yours | В этом лобби версия новее. Выйди из игры и дай Steam обновить твою | in this lobby the version is newer. leave the game and let Steam update yours | top of the screen, stays for the session: the player tried to join a lobby whose host has a newer build; Steam never updates a running game |
| ver.theirs | That lobby runs an older version. Its host has to update the game | В этом лобби версия старее. Его хост должен обновить игру | in this lobby the version is older. its host must update the game | top of the screen for ten seconds: the player tried to join a lobby whose host has an older build |
| ver.differs | That lobby runs a different version of the game | В этом лобби другая версия игры | in this lobby there is another version of the game | top of the screen for ten seconds: the builds differ and it is not known which one is newer (the editor, a build not started from Steam) |
| wand.hosed | The pour is too strong. Step back to draw | Струя чернил слишком сильная. Отойди, чтобы рисовать | the ink jet is too strong. step away to draw | a wizard standing right at the clean pot while it fills: the ink pours into the wand too hard to draw; shown behind a ! keycap |
| menu.extras | Extras | Дополнительно | additionally | main menu button under PLAY: clicking it shows or hides the Map Creator and Photo Booth buttons beneath it |
| chip.release | Release | Отпустить | to let go | chip beside the F key while a ghost rides a spell, a zombie or a golem: pressing it lets go of what it rides |
| chip.up | Up | Вверх | upward | chip beside the SPACE key for a ghost flying free: fly upward (goes away once the key was used) |
| chip.down | Down | Вниз | downward | chip beside the CTRL key for a ghost flying free: fly downward (goes away once the key was used) |
| clip.failed | Recording did not work on this PC | Запись на этом ПК не получилась | the recording on this PC did not come out | shown top right when the I key could not start or finish a video (Windows has no video encoder here, or the file could not be written) |
| clip.disk | Not enough free disk space to record | Не хватает свободного места на диске для записи | not enough free space on the disk for recording | shown top right when the I key is pressed with under 1.5 GB free on the drive videos go to |
| photo.color | Color | Цвет | color | section on a creature in the photo booth: its color sliders |
| photo.color.own | Its own color | Родной цвет | native color | the creature goes back to the color it came with |
| photo.anims | Animations | Анимации | animations | button in the picked thing's window, and the title of the window listing all its animations |
| photo.anim.play | Play | Воспроизвести | play | runs the picked animation, looping |
| photo.anim.pause | Pause | Пауза | pause | stops the animation where it is |
| photo.anim.note | Pick one to play it. Pause and use the time slider to hold any moment. | Выбери, чтобы проиграть. Поставь на паузу и ползунком времени останови любой момент | choose to play. pause, and with the time slider stop any moment | top of the animations window |
| menu.photobooth | Photo Booth | Фотобудка | photo booth | main menu button under Map Creator: opens the saved photo setups |
| photos.title | Photo Booth | Фотобудка | photo booth | title of the screen with the saved photo setups |
| photos.newphoto | New photo | Новое фото | new photo | card that starts a new photo setup; also a new setup's first name |
| photos.newfolder | New folder | Новая папка | new folder | card that makes a folder for photos; also the folder's first name |
| photos.pick | Pick a photo or a folder to see it here | Выбери фото или папку, чтобы увидеть здесь | choose a photo or a folder to see here | right side of the photos screen when nothing is picked |
| photos.count | {0} photos | Фото: {0} | photos: {0} | under a folder's name: how many photo setups it holds |
| photos.open | Open | Открыть | open | opens the picked folder |
| photos.folder | Folder | Папка | folder | drop down: which folder a photo setup sits in |
| photos.nofolder | No folder | Без папки | without folder | a photo setup outside every folder |
| photos.folder.name | Folder name | Имя папки | the folder's name | text field to rename the picked folder |
| photos.folder.new | New folder name | Имя новой папки | name of the new folder | in the booth: text field naming a folder about to be made |
| photos.folder.make | Make the folder | Создать папку | create folder | makes the folder named above and puts this photo setup in it |
| photos.rename | Rename | Переименовать | rename | renames the picked folder to the name typed above |
| photos.folder.delete | Delete folder | Удалить папку | delete folder | deletes the picked folder; only works when it is empty |
| photos.folder.full | Only an empty folder can be deleted | Удалить можно только пустую папку | one can delete only an empty folder | why deleting a folder did nothing: move or delete its photos first |
| photos.taken | That name is taken | Такое имя уже есть | such a name already exists | renaming a folder to a name another folder already has |
| photos.files | Open the photos folder | Открыть папку с фото | open the folder with photos | opens the computer folder where finished photos (PNG files) are saved |
| photo.stored | Saved {0} | Сохранено: {0} | saved: {0} | the photo setup was saved under this name, to open and edit again |
| photo.add | Add | Добавить | add | ribbon button and window: the things that can be placed in the photo |
| photo.ink | Ink | Чернила | ink | ribbon button and window: draw ink on placed things |
| photo.look | Light and look | Свет и вид | light and look | ribbon button and window: the sun, the back light and the picture sliders |
| photo.photo | Photo | Фото | photo | ribbon button and window: name, folder, canvas size, what is behind, where it is taken |
| photo.take | Take the photo | Сделать фото | take a photo | the shutter: saves the picture as a PNG at the canvas size |
| photo.new | New photo | Новое фото | new photo | in the booth: a fresh setup on the same backdrop, from the same camera |
| photo.help | Right mouse looks, middle mouse drags the view, {0} flies, Space rises and Ctrl sinks, the wheel moves forward and back. Left click picks a thing up to drag it. Ctrl+Z undoes and Ctrl+R redoes. | Правая кнопка мыши: осмотреться. Средняя кнопка: тащить вид. {0}: полёт. Пробел: вверх, Ctrl: вниз, колесо: вперёд и назад. Левый клик: схватить что-то и тащить. Ctrl+Z: отменить, Ctrl+R: вернуть. | right mouse button: look around. middle button: drag the view. {0}: flight. space: up, Ctrl: down, wheel: forward and back. left click: grab something and drag. Ctrl+Z: undo, Ctrl+R: bring back. | booth controls; {0} = the four fly keys as printed on this keyboard |
| photo.hint.idle | Pick something in Add, then click the ground | Выбери что-нибудь в «Добавить» и кликни по земле | choose something in 'Add' and click on the ground | hint under the ribbon when nothing is going on |
| photo.hint.place | Click the ground to put down {0} as many times as you like. Esc stops | Кликай по земле, чтобы ставить {0} сколько хочешь. Esc отменяет | click on the ground to put {0} as much as you want. Esc cancels | {0} = the picked thing's name |
| photo.hint.picked | Drag it to move it, Shift drag lifts it, {0} turns it, {1} {2} size it, Delete removes it | Тащи: сдвинуть. Shift и тащи: поднять. {0}: повернуть. {1} {2}: размер. Delete: удалить | drag: move. Shift and drag: lift. {0}: turn. {1} {2}: size. Delete: delete | {0} = the turn key, {1} {2} = smaller and bigger keys, as printed on this keyboard |
| photo.hint.pose | Drag a hand or a foot. Shift drag turns one bone, the wheel twists it. Esc when done | Тащи руку или ногу. Shift и тащи: крутить одну кость, колесо её скручивает. Esc, когда готово | drag a hand or a leg. Shift and drag: turn one bone, the wheel twists it. Esc when ready | hint while posing a character |
| photo.hint.ink | Draw on anything you placed. Esc when done | Рисуй на всём, что поставил. Esc, когда готово | draw on everything you placed. Esc when ready | hint while the ink pen is out |
| photo.taken | Photo saved: {0} | Фото сохранено: {0} | photo saved: {0} | the picture file was written; {0} = its file name |
| photo.failed | The photo did not work | Фото не получилось | the photo did not come out | taking or saving the picture failed |
| photo.add.note | Pick one, then click the ground. Pick it again to stop. | Выбери и кликни по земле. Выбери снова, чтобы перестать | choose and click on the ground. choose again to stop | top of the Add window |
| photo.add.characters | Characters | Персонажи | characters | Add section: a wizard or an acolyte |
| photo.add.creatures | Creatures | Существа | creatures | Add section: zombies, golems and the book's creatures |
| photo.add.areas | Areas | Области | areas | Add section: the areas spells leave behind |
| photo.add.effects | Effects | Эффекты | effects | Add section: explosions, sparkles, comic words |
| photo.add.pieces | Island pieces | Детали острова | parts of the island | Add section: houses, trees and props of the island, by biome |
| photo.add.inside | Inside houses | В домах | in the houses | the pieces that furnish the inside of houses |
| photo.add.empty | Nothing here yet | Тут пока пусто | it is empty here for now | an Add section with nothing to place |
| photo.lift | Off the ground | Над землёй | above the ground | slider: how high above the ground the picked thing floats, in metres |
| photo.tilt | Tilt | Наклон | tilt | slider: tips the picked thing forward or back |
| photo.roll | Roll | Крен | roll, bank | slider: tips the picked thing to the side |
| photo.time | Time | Время | time | slider: how far the picked thing's effect or animation has run; drag it to find the best moment |
| photo.side | Side | Сторона | side | switch: the character is a wizard or an acolyte |
| photo.outfit.mine | My outfit | Мой наряд | my outfit | dresses the character in the player's own outfit |
| photo.outfit.shuffle | Shuffle outfit | Случайный наряд | random outfit | dresses the character in random clothes |
| photo.hat | Hat color | Цвет шляпы | color of the hat | section with the hat color sliders |
| photo.hat.hue | Hue | Оттенок | hue | hat color slider: which color of the rainbow |
| photo.hat.sat | Color strength | Насыщенность | saturation | hat color slider: from grey to full color |
| photo.hat.light | Brightness | Яркость | brightness | hat color slider: from dark to light |
| photo.hat.mine | My hat color | Мой цвет шляпы | my hat color | the player's own saved hat color |
| photo.eyes | Eyes | Глаза | eyes | switch: the eyes' mood |
| photo.mood.neutral | Calm | Спокойные | calm, of eyes | eyes mood |
| photo.mood.scared | Scared | Испуганные | frightened, of eyes | eyes mood |
| photo.mood.wowed | Wowed | Восхищённые | amazed, of eyes | eyes mood |
| photo.mood.mad | Mad | Злые | angry, of eyes | eyes mood |
| photo.mood.dizzy | Dizzy | Ошалелые | dazed, of eyes | eyes mood: the pupils cross |
| photo.gaze | Looking | Взгляд | gaze | switch: where the eyes look |
| photo.gaze.camera | At the camera | В камеру | into the camera | the eyes look into the camera |
| photo.gaze.ahead | Straight ahead | Прямо | straight | the eyes look where the body faces |
| photo.body | Body | Тело | body | section: height, width, head, arms and legs sliders, as in the Creature Creator |
| photo.pose.start | Pose it | Поставить позу | set a pose | starts dragging the picked character's arms and legs |
| photo.pose.stop | Done posing | Поза готова | the pose is ready | stops posing the character |
| photo.pose.relax | Stand normal | Просто стоять | just stand | the character goes back to standing: no animation, no pose |
| photo.pose.saved | Saved poses | Сохранённые позы | saved poses | drop down: the player's own poses saved in pose mode |
| photo.pose.none | No saved poses yet. Save some in pose mode. | Сохранённых поз пока нет. Сохрани их в режиме позы | no saved poses yet. save them in pose mode | the player has not saved a pose in pose mode |
| photo.ink.note | Draw on anything you placed. The ink rides the part it lands on. | Рисуй на всём, что поставил. Чернила держатся за ту часть, куда попали | draw on everything you placed. the ink holds on to the part where it landed | top of the Ink window |
| photo.ink.draw | Draw | Рисовать | draw | takes out the pen |
| photo.ink.stop | Stop drawing | Хватит рисовать | enough drawing | puts the pen away |
| photo.ink.ink | Wizard ink | Чернила мага | ink of the mage | ink color: the wizards' dark blue ink |
| photo.ink.green | Acolyte ink | Чернила аколита | ink of the acolyte | ink color: the acolytes' green ink |
| photo.ink.gold | Seal gold | Золото печати | gold of the seal | ink color: the gold of a seal |
| photo.ink.blue | Rune blue | Синева руны | blue of the rune | ink color: the bright blue of a rune |
| photo.ink.white | White | Белый | white | ink color |
| photo.ink.black | Black | Чёрный | black | ink color |
| photo.ink.red | Red | Красный | red | ink color |
| photo.ink.width | Thickness | Толщина | thickness | slider: how thick the ink line is, in millimetres |
| photo.ink.undo | Take back the last line | Убрать последнюю линию | remove the last line | removes the last ink line drawn |
| photo.ink.wipepicked | Wipe the picked one | Стереть с выбранного | wipe from the chosen one | removes every ink line from the picked thing |
| photo.ink.wipe | Wipe all ink | Стереть все чернила | erase all the ink | removes every ink line in the photo |
| photo.light | Light | Свет | light | section: the sun and the back light |
| photo.sun.turn | Sun turn | Поворот солнца | turn of the sun | slider: where around the sky the sun stands |
| photo.sun.height | Sun height | Высота солнца | height of the sun | slider: how high the sun stands; low makes long shadows |
| photo.sun.power | Sun strength | Сила солнца | strength of the sun | slider: how bright the sun is |
| photo.sun.reset | Put the sun back | Вернуть солнце | return the sun | the sun goes back to where the island had it |
| photo.rim | Back light | Контровой свет | back light | slider: a light from behind the subjects that makes their edges shine |
| photo.rim.turn | Back light turn | Поворот контрового света | turn of the back light | slider: which side the back light comes from |
| photo.picture | Picture | Картинка | picture | section: brightness, contrast, color and the other picture sliders |
| photo.brightness | Brightness | Яркость | brightness | picture slider |
| photo.contrast | Contrast | Контраст | contrast | picture slider |
| photo.warmth | Warmth | Теплота | warmth | picture slider: bluer or more orange |
| photo.saturation | Color | Насыщенность | saturation | picture slider: from grey to very colorful |
| photo.glow | Glow | Сияние | radiance | picture slider: bright things shine and spill light |
| photo.vignette | Dark corners | Тёмные углы | dark corners | picture slider: darkens the picture's edges |
| photo.blur | Background blur | Размытый фон | blurred background | picture slider: what is far from the sharp distance goes soft |
| photo.focus | Sharp at | Резко на | sharp at | slider: the distance that stays sharp when the background is blurred, in metres |
| photo.focus.picked | Make the picked one sharp | Навести резкость на выбранное | focus on the chosen one | sets the sharp distance to the picked thing |
| photo.lens | Lens | Объектив | lens | slider: how wide the camera sees, in degrees |
| photo.look.reset | Reset the look | Сбросить вид | reset the look | the picture sliders go back to zero |
| photo.canvas | Canvas | Холст | canvas | heading: the photo's size in pixels |
| photo.width | Width | Ширина | width | the photo's width in pixels |
| photo.height | Height | Высота | height | the photo's height in pixels |
| photo.sizes | Sizes for | Размеры для | sizes for | drop down of ready sizes by platform (YouTube, Steam...) |
| photo.safe.note | The white box is Steam's safe area | Белая рамка: безопасная зона Steam | white frame: Steam's safe zone | the library hero size shows Steam's inner safe box |
| photo.seethrough.note | Steam wants this one with nothing behind | Steam хочет это без фона | Steam wants this without a background | the library logo size: Steam wants a picture without background |
| photo.back | Behind | Фон | background | switch: what is behind the subjects in the photo |
| photo.back.world | The world | Мир | world | behind the subjects: the island as it is |
| photo.back.colour | A color | Цвет | color | behind the subjects: one flat color |
| photo.back.none | Nothing | Ничего | nothing | behind the subjects: nothing, a see-through PNG |
| photo.back.none.note | Nothing behind: the grey is not in the photo. Glow and blur stay off so the edges stay clean. | Без фона: серого не будет на фото. Сияние и размытие выключены, чтобы края были чистыми | without background: the grey will not be on the photo. radiance and blur are off so the edges are clean | under the Behind switch when Nothing is picked |
| photo.red | Red | Красный | red | background color slider |
| photo.green | Green | Зелёный | green | background color slider |
| photo.blue | Blue | Синий | blue | background color slider |
| photo.saved | Photos in this folder | Фото в этой папке | photos in this folder | section listing the other setups in the same folder, to hop between them |
| photo.saved.none | No saved photos here yet | Тут пока нет сохранённых фото | there are no saved photos here yet | the folder has no saved setups |
| photo.size.youtube | YouTube thumbnail | Превью для YouTube | preview for YouTube | canvas size 1280x720 |
| photo.size.hd | Full HD | Full HD | full HD | canvas size 1920x1080 |
| photo.size.4k | 4K | 4K | 4K | canvas size 3840x2160 |
| photo.size.square | Square post | Квадратный пост | square post | canvas size 1080x1080 for social posts |
| photo.size.portrait | Tall post | Вертикальный пост | vertical post | canvas size 1080x1350 for social posts |
| photo.size.story | Story, Reels, Shorts, TikTok | Истории, Reels, Shorts, TikTok | stories, Reels, Shorts, TikTok | canvas size 1080x1920 for tall phone videos and stories |
| photo.size.x | X post | Пост в X | post in X | canvas size 1600x900 for a post on X |
| photo.size.steamheader | Steam header | Шапка Steam | Steam header | canvas size 920x430, the store and library header |
| photo.size.steamsmall | Steam small capsule | Малая капсула Steam | small Steam capsule | canvas size 462x174 |
| photo.size.steammain | Steam main capsule | Главная капсула Steam | main Steam capsule | canvas size 1232x706 |
| photo.size.steamvertical | Steam vertical capsule | Вертикальная капсула Steam | vertical Steam capsule | canvas size 748x896 |
| photo.size.steampage | Steam page background | Фон страницы Steam | Steam page background | canvas size 1438x810 |
| photo.size.librarycapsule | Steam library capsule | Капсула библиотеки Steam | capsule of the Steam library | canvas size 600x900 |
| photo.size.libraryhero | Steam library hero | Главное изображение библиотеки Steam | main image of the Steam library | canvas size 3840x1240, the wide library banner |
| photo.size.librarylogo | Steam library logo | Логотип библиотеки Steam | logo of the Steam library | canvas size 1280x720, a logo with nothing behind |
| end.potdry | The pot ran dry | Котёл опустел | the cauldron went empty | the ending camera looks at the empty pot; acolytes win |
| end.nowizards | No wizard left standing | Ни одного мага на ногах | not one wizard on their feet | the ending camera looks at a fallen wizard; acolytes win |
| end.sweep | Every acolyte caught, the pot still clean | Все аколиты пойманы, котёл всё ещё чистый | all acolytes caught, the cauldron still clean | the ending camera looks at a fallen acolyte; wizards win |
| end.greenbell | Time's up and the pot is green | Время вышло, котёл зелёный | the time is out, the cauldron is green | the ending camera looks at the green pot; acolytes win |
| end.cleanbell | Time's up and the pot is clean | Время вышло, котёл чистый | the time is out, the cauldron is clean | the ending camera looks at the clean pot; wizards win |
| end.bossdown | The boss fell | Босс пал | the boss fell | the ending camera looks where the last boss fell |
| end.everyonedown | Everyone went down | Все пали | all fell | the ending camera looks at a fallen player; the boss or the environment wins |
| end.timeup | Time's up | Время вышло | the time is out | the ending camera looks at the boss that outlasted the clock |
| mc.official | This map comes with the game. Give it a new name to save your own copy. | Эта карта идёт вместе с игрой. Дай ей новое имя, чтобы сохранить свою копию. | this map comes together with the game. give it a new name to save your copy | Map Creator, pressing Save on a map that ships with the game |
| maps.official | Comes with the game | Идёт вместе с игрой | comes together with the game | Maps screen, on a map that ships with the game (no Share or Delete there) |
| mc.spell.kind | Kind | Вид | kind | switch label in the Spells screen: a spell or a summon |
| mc.kind.spell | Spell | Заклинание | spell | option: a spell that is its numbers |
| mc.kind.summon | Summon | Призыв | summon | option: a spell that raises a creature; also the tag after its name in lists |
| mc.summons | Summons | Призывает | summons | drop down on a summon: which creature it raises |
| mc.summons.none | Make a creature in the Creature Creator first | Сначала создай существо в редакторе существ | first create a creature in the editor of creatures | shown when Summon is picked and the map has no creatures |
| mc.note.summonlook | A summon looks like the creature it raises. Change its look in the Creature Creator. | Призыв выглядит как существо, которое он призывает. Внешний вид меняй в редакторе существ. | a summon looks like the creature it summons. change the appearance in the editor of creatures | the look column of a summon |
| mc.note.handles | Drag to turn it, the wheel zooms. Drag a colored square to change its shape. | Тащи, чтобы вращать, колесо приближает. Тащи цветной квадратик, чтобы менять форму. | drag to rotate, the wheel zooms in. drag a colored little square to change the shape | note under a golem creature's preview |
| mc.shape.reset | Reset shape | Сбросить форму | reset the shape | button: the creature's body back to its plain shape |
| mc.build.height | Height | Рост | height (of a body) | slider on a zombie body creature |
| mc.build.width | Width | Ширина | width | slider on a zombie body creature |
| mc.build.head | Head | Голова | head | slider on a zombie body creature: head size |
| mc.build.arms | Arms | Руки | arms | slider on a zombie body creature: arm size |
| mc.build.legs | Legs | Ноги | legs | slider on a zombie body creature: leg size |
| mc.note.size | Times its body's own size. A seal or a placement makes it bigger or smaller on top. | Во сколько раз больше своего тела. Печать или расстановка делают его ещё больше или меньше. | how many times bigger than its own body. a seal or the placing make it even bigger or smaller | note under the creature's Size |
| mc.teams | Teams | Команды | teams | Map window section: the map's teams |
| mc.teams.players | Players | Игроки | players | switch label: are the players two teams or one |
| mc.teams.split | Two teams | Две команды | two teams | option: wizards and acolytes are two teams |
| mc.teams.together | One team | Одна команда | one team | option: every player, wizard or acolyte, is on one team |
| mc.counts.everyone | Players count | Игроки борются за победу | players fight for victory | switch: the players' one team plays to win (off = it just plays along) |
| mc.counts.wizards | Wizards count | Маги борются за победу | wizards fight for victory | switch: the wizards' team plays to win (off = it just plays along) |
| mc.counts.acolytes | Acolytes count | Аколиты борются за победу | acolytes fight for victory | switch: the acolytes' team plays to win (off = it just plays along) |
| mc.counts.environment | Environment counts | Окружение борется за победу | the environment fights for victory | switch: the environment's team (the map's own creatures) plays to win |
| mc.note.teams | Teams that count play to win. The last one standing wins. The environment stands while a boss lives, and wins when time runs out. | Команды, которые борются за победу, сражаются, и побеждает последняя оставшаяся. Окружение держится, пока жив босс, и побеждает, когда время выходит. | teams that fight for victory battle, and the last one remaining wins. the environment holds while the boss is alive, and wins when the time runs out. | note under the team switches |
| mc.note.noboss | The environment counts but has no boss yet. Turn on Boss for a creature in the Creature Creator and place it. | Окружение борется за победу, но босса пока нет. Включи «Босс» у существа в редакторе существ и поставь его. | the environment fights for victory, but there is no boss yet. switch on boss for a creature in the editor of creatures and place it. | warning under the team switches when no boss is placed |
| mc.boss | Boss | Босс | boss | switch in the Creature Creator: this creature is a boss |
| mc.note.boss | Everyone sees a boss's health. The environment stands while a boss lives. | Все видят здоровье босса. Пока жив босс, окружение держится. | everyone sees the boss's health. while the boss is alive, the environment holds. | note under the Boss switch |
| round.everyone | EVERYONE WINS | ПОБЕДИЛИ ВСЕ | all won | end banner: the players' one team won |
| round.bosswins | THE BOSS WINS | БОСС ПОБЕДИЛ | boss won | end banner: the environment's team won (every player a ghost, or time ran out) |
| round.nobody | NOBODY WINS | НИКТО НЕ ПОБЕДИЛ | nobody won | end banner: the match ended and no team that counts won |
| round.beatboss | BEAT THE BOSS | ПОБЕДИТЕ БОССА | defeat the boss | start banner on a map whose environment counts |
| round.playaround | JUST HAVE FUN | ПРОСТО ВЕСЕЛИТЕСЬ | just have fun | start banner on a map where no one plays to win against another team |
| mc.creatures.open | Creature Creator | Редактор существ | editor of creatures | button in the Objects window, and the title of the creature screen |
| mc.creatures.note | These creatures belong to this map. Place them from the Objects window or raise them with a summon. | Эти существа принадлежат этой карте. Ставь их из окна «Объекты» или вызывай призывом. | these creatures belong to this map. place them from the objects window or call them with a summon | note over the creature list |
| mc.creature.new | New creature | Новое существо | new creature | button over the creature list; also the new creature's first name |
| mc.creature.sure | Sure? The placed ones go too | Точно? Поставленные тоже исчезнут | sure? the placed ones will vanish too | Delete after the first click: deleting a creature removes the ones placed on the map |
| mc.creature.none | Pick a creature or make a new one | Создай существо или выбери из списка | create a creature or choose from the list | numbers column when no creature is picked |
| mc.creature.edit | Edit this creature | Изменить это существо | change this creature | button on a placed creature in the Objects window |
| mc.creature.body | Body | Тело | body | switch: the creature is a golem or a zombie body |
| mc.creature.preview | Drag to turn it, the wheel zooms. The color follows its numbers. | Тащи, чтобы вращать, колесо приближает. Цвет следует за его числами. | drag to rotate, the wheel zooms in. the color follows its numbers | note under the creature preview |
| mc.behaviour | Behavior | Поведение | behavior | section heading: how the creature acts |
| mc.behaviour.roams | Roams | Бродит | wanders | behavior choice: wanders and fights what it notices |
| mc.behaviour.hunts | Hunts | Охотится | hunts | behavior choice: goes after every enemy, never runs |
| mc.behaviour.guards | Guards | Охраняет | guards | behavior choice: stays near where it stood up |
| mc.behaviour.skittish | Skittish | Пугливое | skittish | behavior choice: runs from wands and spells |
| mc.note.roams | Wanders and fights what it notices. A zombie still runs from wands. | Бродит и дерётся с тем, что заметит. Зомби всё равно бегут от палочек. | wanders and fights with what it notices. zombies still run from wands | note under the Roams choice |
| mc.note.hunts | Goes after every enemy it notices and never runs away. | Гонится за каждым замеченным врагом и никогда не убегает. | chases every noticed enemy and never runs away | note under the Hunts choice |
| mc.note.guards | Stays near where it stood up, fights whoever comes close and never runs. | Держится рядом с местом, где поднялось, дерётся с теми, кто подходит, и никогда не убегает. | keeps near the place where it rose, fights those who come near and never runs away | note under the Guards choice |
| mc.note.skittish | Runs from wands and spells, and fights when cornered. | Бежит от палочек и заклинаний и дерётся, когда загнано в угол. | runs from wands and spells and fights when driven into a corner | note under the Skittish choice |
| mc.guardrange | Guard distance | Радиус охраны | radius of guarding | slider under Guards: how far from its spot it goes after someone, shown with m |
| mc.note.golemcharge | A golem always charges at what it fights. | Голем всегда делает рывок на того, с кем дерётся. | a golem always makes a dash at the one it fights | Can do section when the body is a golem |
| mc.note.creaturearea | What a golem drops around itself as it walks. A zombie body drops nothing. | Что голем роняет вокруг себя на ходу. Тело зомби ничего не роняет. | what a golem drops around itself on the move. a zombie's body drops nothing | note under Area in the Creature Creator |
| mc.back.creature | Back to the creature | Назад к существу | back to creature | closes the area screen, back to the Creature Creator |
| mc.needbiome | A map needs at least 1 biome | Карте нужен хотя бы 1 биом | to-map needed at least 1 biome | Map window note under a greyed Save; also said when saving, sharing or keeping a map with no biome |
| mc.back.menu | Back to the menu | Назад в меню | back to menu | the creator's own button, top left: leaves for the main menu |
| mc.back.spell | Back to the spell | Назад к заклинанию | back to spell | closes the area screen, back to the Spells creator |
| mc.objects.creatures | Creatures | Существа | creatures | Objects window group: the bodies from the map's spellbook |
| mc.objects.inside | Inside houses | Внутри домов | inside of-houses | Objects window group: the pieces that fill houses |
| mc.shape.wearing | This spell wears | Форма этого заклинания | shape of-this spell | drop down of saved shapes; shows as This spell wears: <shape> |
| mc.summon.addrune | Add a rune | Добавить руну | add rune | drop down of every rune; a pick adds it to Summoned by |
| mc.area.open | Open area | Открыть область | open area | opens the picked area on its own screen |
| mc.area.look | Look | Вид | look | drop down on the area screen: which effect the area wears, from the game's looks |
| mc.note.look | A fire effect, a lightning effect, a rock, a trail. Anything. | Эффект огня, молнии, камень, след. Что угодно. | effect of-fire, of-lightning, stone, trace. whatever | note under Look |
| mc.note.areatint | Colored by the spell that carries it. | Цвет даёт заклинание, которое её несёт. | color gives spell, which it carries | under the area's preview |
| mc.area.nolook | No look yet. Pick one on the right | Вида пока нет. Выбери справа | of-look so-far none. choose on-right | the area preview while the area wears nothing |
| mc.emoji.pick | Pick an emoji | Выбрать эмодзи | choose emoji | opens and closes the emoji grid |
| mc.keep.update | Keep the changes | Оставить изменения | keep changes | the pad holds a kept drawing: its new strokes replace it |
| mc.page.brush | Brush size | Размер кисти | size of-brush | page painter slider, in page pixels |
| mc.page.stamp | Stamp the rune on click | Штамп руны по клику | stamp of-rune on click | page painter switch: a click sets the rune's drawing down |
| mc.page.stampsize | Stamp size | Размер штампа | size of-stamp | page painter slider: share of the page height |
| mc.page.stampturn | Stamp turn | Поворот штампа | turn of-stamp | page painter slider: degrees |
| mc.page.noglyph | This rune has no drawing to stamp yet | У этой руны пока нет рисунка для штампа | at this rune so-far no drawing for stamp | the stamp is on but the rune has no drawing |
| mc.back.map | Back to the map | Назад к карте | back to map | closes the full-screen Spells or Runes creator |
| mc.preview.note | Drag to turn it, the wheel zooms. Drag a square to reshape the body. The color follows the sliders. | Тащи, чтобы вращать, колесо приближает. Тащи квадратик, чтобы менять форму тела. Цвет следует за ползунками. | drag to rotate, wheel zooms-in. drag a little-square to change shape of-body. color follows the sliders | under the spell's live preview; the squares are its bones |
| mc.shape.save | Save shape | Сохранить форму | save shape | saves the posed bones and the material sliders under the typed name |
| mc.shape.delete | Delete shape | Удалить форму | delete shape | removes the saved shape; spells wearing it fall back to the plain body |
| mc.spell.none | Make a spell or pick one from the list | Создай заклинание или выбери из списка | create spell or choose from list | the Spells creator with nothing picked |
| mc.note.summoned | The runes a seal must hold to raise this. The same rune twice means twice, so two of one rune can raise what one cannot. | Руны, которые должны быть в печати, чтобы призвать это. Одна и та же руна дважды считается дважды, так что две призовут то, что не может одна. | runes which must be in seal to summon this. one and the same rune twice counts twice, so two will-summon what cannot one | note under Summoned by, for a Zombie or Golem spell |
| mc.summon.all | All twelve | Все двенадцать | all twelve | summoned by every one of the twelve game runes |
| mc.note.bornas | Its natural state. It drifts from here like anything in the world: a zombie born hot is at home in fire. | Его природное состояние. Отсюда оно меняется, как всё в мире: зомби, рождённый горячим, в огне как дома. | its natural state. from-here it changes, like everything in world: zombie, born hot, in fire as at-home | note under Born as |
| mc.casts.all | Every spell | Все заклинания | all spells | button: the creature casts every spell of the map (a creature has no book now) |
| mc.note.conditions | The conditions that need to be met for the spell to exist: what it is, and what it gives to whatever it touches. | Условия, при которых заклинание существует: что оно такое и что даёт всему, чего касается. | conditions, under which spell exists: what it such and what gives to-everything, that it-touches | note under Conditions, for a Particle spell |
| mc.note.effects | Byproducts a spell may carry, never needed to make it. They ride the same numbers and land on whatever the spell touches. | Побочные эффекты, которые заклинание может нести; для его создания они не нужны. Они идут на тех же числах и достаются всему, чего коснётся заклинание. | side effects, which spell can carry; for its creation they not needed. they go on the same numbers and go to-everything, that touches spell | note under Effects, for a Particle spell |
| mc.verdict.nothing | Every axis is zero. This spell is nothing. | Все оси на нуле. Это заклинание ничто. | all axes at zero. this spell nothing | the spell said back: nothing authored |
| mc.verdict.reach | When something's numbers reach these, it becomes this. | Когда числа чего-то достигают этих, оно становится этим. | when numbers of-something reach these, it becomes this | the spell said back: its first line |
| mc.verdict.area | It carries an area, which outlives it. | Оно несёт область, которая переживает его. | it carries area, which outlives it | the spell said back: it has an area |
| mc.verdict.locked | Its locked axes hold against the world and do not spend on impact. | Его закреплённые оси держатся против мира и не тратятся при ударе. | its fixed axes hold against world and not spent at impact | the spell said back: it has axes locked as a biome |
| mc.verdict.spends | It spends itself on whatever it touches. | Оно тратит себя на всё, чего касается. | it spends itself on everything, that it-touches | the spell said back: no area, nothing locked |
| mc.verdict.physical | Force can destroy it, and what it still holds scatters. | Сила может его разрушить, и то, что в нём ещё есть, разлетится. | force can it destroy, and that, what in it still is, will-scatter | the spell said back: it has Strength |
| mc.verdict.ghost | Force cannot touch it. It only goes out when its numbers run down. | Сила его не трогает. Оно гаснет, только когда кончаются его числа. | force it not touches. it goes-out, only when end its numbers | the spell said back: no Strength |
| mc.note.area | The effect that rides on this spell. It works from the same numbers. | Эффект, который едет на этом заклинании. Он работает на тех же числах. | effect, which rides on this spell. it works on the same numbers | note under Area |
| mc.area.new | New area | Новая область | new area | makes a new area for this spell |
| mc.area.named | {0} area | Область: {0} | area: {0} | a new area's first name; {0} the spell's name |
| mc.area.loadspell | Load a spell | Загрузить заклинание | load spell | over the grid: the area becomes the picked spell |
| mc.note.loadspell | The area becomes that spell: its numbers replace what the spell hands down. | Область становится этим заклинанием: его числа заменяют то, что передаёт заклинание. | area becomes this spell: its numbers replace that, what passes spell | note under Load a spell |
| mc.area.trailwidth | Trail width | Ширина следа | width of-trail | area slider: how wide its trail is |
| mc.area.traillasts | Trail lasts, seconds | След держится, секунды | trail holds, seconds | area slider: how long its trail stays |
| mc.area.start | Where it starts | Где она начинается | where it starts | heading over the area's offset from the spell |
| mc.note.start | It rushes back to the spell from here. Put Y at 20 and it falls from the sky. | Отсюда она мчится обратно к заклинанию. Поставь Y на 20, и она упадёт с неба. | from-here it rushes back to spell. set Y on 20, and it will-fall from sky | note under Where it starts; Y is the up slider |
| mc.area.spreading | Spreading | Распространяется | spreads | area switch: it appears again nearby |
| mc.note.spreading | Appears again on nearby things that meet the same condition. | Появляется снова на ближних вещах, подходящих под то же условие. | appears again on near things, fitting under the same condition | note under Spreading |
| mc.area.delete | Delete area | Удалить область | delete area | removes the area; spells carrying it carry none (asks with a second click) |
| mc.note.material | Everything starts still. Move a slider to add movement. | Всё начинается неподвижным. Сдвинь ползунок, чтобы добавить движение. | everything starts motionless. move slider to add movement | note under Material |
| mc.page | Page | Страница | page | the Runes creator's second tab: the grimoire page |
| mc.page.note | The page this rune shows in the grimoire. Left mouse paints, right mouse paints paper. It is saved with the map | Страница этой руны в гримуаре. Левая кнопка мыши рисует, правая закрашивает бумагой. Сохраняется вместе с картой | page of-this rune in grimoire. left button of-mouse draws, right paints-over with-paper. is-saved together with map | over the page painter |
| mc.side | Side | Сторона | side | which side the emoji and page belong to: Wizard or Acolyte |
| mc.side.note | The emoji and the page belong to this side. An acolyte with none of its own shows the wizard's. | Эмодзи и страница принадлежат этой стороне. Если у аколита нет своих, показываются те, что у мага. | emoji and page belong to-this side. if at acolyte no own-ones, are-shown those, which at wizard | note under the side switch |
| mc.pushes.note | What drawing this rune adds to the seal's numbers, one axis or several. The spells decide what that becomes. | Что рисунок этой руны добавляет к числам печати, по одной оси или по нескольким. Во что это превратится, решают заклинания. | what drawing of-this rune adds to numbers of-seal, along one axis or along several. into what this will-turn, decide spells | note under Pushes |
| mc.rune.none | Make a rune or pick one from the list | Создай руну или выбери из списка | create rune or choose from list | the Runes creator with nothing picked |
| opt.stick | Stick sensitivity: {0} | Чувствительность стика: {0} | sensitivity of-the-stick: {0} | the controller's right stick, under Look sensitivity |
