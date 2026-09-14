# Turkish (tr) review sheet

File: `Assets/StreamingAssets/Loc/sz_loc_tr.json`. Register: sen, spoken
imperatives. Turkish writes the percent sign before the number (%50), the
file does that: `%{0}`. Turkish needs the letters ı ğ ş İ ç ö ü in the font;
if the hand-drawn skin font lacks them, fill the OtherFont slot on UISkin.

| key | English | translation | literal back-translation | notes |
|---|---|---|---|---|
| door.open | open the door | kapıyı aç | the door, open | |
| door.close | close the door | kapıyı kapat | the door, close | |
| pickup.weapon | pick up the weapon | silahı al | the weapon, take | |
| pickup.full | hands full, drop one first | ellerin dolu, önce bir şey bırak | your hands are full, first drop something | |
| chest.try | try the mystery chest | gizemli sandığı dene | the mysterious chest, try | |
| perk.drink | drink {0} | {0} iç | {0} drink | |
| perk.brewed | {0} is already brewed | {0} zaten hazır | {0} already ready | "brewed" dropped |
| grimoire.open | open the grimoire | büyü kitabını aç | the spell book, open | büyü kitabı = spell book; "grimuar" is not a Turkish word |
| grimoire.close | close the grimoire | büyü kitabını kapat | the spell book, close | |
| chip.done | done | bitti | finished | |
| carry.down | put it down | yere bırak | leave on the ground | |
| scan.aim | scan it, become it | tara, ona dönüş | scan, turn into it | |
| absorb.aim | absorb it, learn its rune | em, rünü öğren | absorb, learn the rune | em = absorb, suck in |
| chest.open | open the chest | sandığı aç | the chest open |  |
| chip.grimoire | grimoire | büyü kitabı | spell book | |
| chip.paint | paint your body | bedenini boya | your body, paint | |
| chip.first | first person | birinci şahıs | first person | |
| chip.third | third person | üçüncü şahıs | third person | |
| chip.pose | pose your wizard | poz ver | give a pose | "your wizard" dropped |
| chip.watch | watch your dead | ölülerini izle | your dead, watch | |
| chip.become | become it again | yine ona dönüş | again turn into it | |
| chip.melt | melt back to idle | kendine dön | return to yourself | "melt" and "idle" dropped |
| chip.precise | faster drawing | daha hızlı çiz | draw faster | |
| chip.erase | erase ink | mürekkebi sil | the ink, erase | |
| chip.absorb | absorb it | em | absorb | |
| hint.alt | hold ALT to draw faster | daha hızlı çizmek için ALT tuşunu basılı tut | to draw faster, hold the ALT key pressed | |
| hint.combine | draw more runes inside of the same seal to combine them | aynı mührün içine daha çok rün çiz, birleşirler | into the same seal draw more runes, they merge | |
| hint.lift | draw ink on things and press E to lift them | eşyaların üstüne mürekkep çiz ve kaldırmak için E tuşuna bas | draw ink on top of objects and to lift press the E key | |
| hint.erase | erasing returns the ink to your wand | silmek mürekkebi asana geri verir | erasing gives the ink back to your wand | asa = wand, staff |
| hint.body | press R to paint runes on your own body | kendi bedenine rün çizmek için R tuşuna bas | to draw runes on your own body press the R key | |
| hint.pose | striking a pose can close a body seal and cast it | bir poz, bedendeki mührü kapatıp büyüyü atabilir | a pose can close the seal on the body and throw the spell | büyü atmak = cast a spell, literally "throw magic" |
| hint.size | bigger runes make stronger spells | büyük rünler daha güçlü büyü yapar | big runes make stronger magic | |
| hint.touch | lines count as one drawing only when they touch | çizgiler ancak birbirine değerse tek çizim sayılır | lines only if touching each other count as one drawing | |
| hint.declare | the book can name a drawing that reads wrong | yanlış okunan çizime kitap ad verebilir | to a wrongly read drawing the book can give a name | |
| hint.trance | fresh ink puts zombies in a trance | taze mürekkep zombileri hipnotize eder | fresh ink hypnotizes zombies | "trans" is rare in Turkish, hipnotize is everyday |
| hint.wake | throw a sleeping spell to wake it | uyuyan bir büyüyü fırlat, uyanır | throw a sleeping spell, it wakes | |
| hint.ghost | the dead rise as ghosts. fly home to your body and a friend can revive you | ölüler hayalet olur. bedenine geri uç, bir arkadaşın seni diriltebilir | the dead become ghosts. fly back to your body, a friend of yours can revive you | |
| hint.doors | doors open when you walk into them | kapılar, üstlerine yürüyünce açılır | doors, when you walk onto them, open | |
| paint.done | done painting | boyama bitti | painting finished | |
| paint.pose | strike a pose | poz ver | give a pose | |
| paint.orbit | orbit | döndür | rotate | |
| hat.pillar | pick your hat color | şapka rengini seç | the hat color, choose | |
| side.pillar | change your side | taraf değiştir | side change | |
| hat.done | done | bitti | finished | |
| shape.back | back to yourself | kendine dön | return to yourself | |
| shape.turn | turns you | seni döndürür | turns you | |
| shape.save | saves | kaydeder | saves | |
| shape.recall | recalls | geri çağırır | calls back | |
| menu.resume | Resume | Devam et | continue | |
| menu.restart | Restart run | Baştan başla | start from the beginning | "run" dropped |
| menu.options | Options | Ayarlar | settings | |
| menu.wishlist | ♥ Wishlist on Steam | ♥ Steam istek listesine ekle | ♥ add to the Steam wish list | istek listesi is Steam Turkey's term |
| menu.quit | Quit | Çık | exit | |
| menu.back | Back | Geri | back | |
| opt.sens | Look sensitivity: {0} | Bakış hassasiyeti: {0} | look sensitivity: {0} | |
| opt.volume | Volume: {0}% | Ses: %{0} | sound: %{0} | percent sign before the number |
| opt.language | Language: {0} | Dil: {0} | language: {0} | |
| opt.immersive.on | Immersive mode: ON | Derin mod: AÇIK | deep mode: OPEN | "immersive" has no everyday Turkish word; derin = deep; AÇIK/KAPALI = on/off |
| opt.immersive.off | Immersive mode: OFF | Derin mod: KAPALI | deep mode: CLOSED | |
| opt.immersive.hint | no HUD at all. for players who know the game | hiç arayüz yok. oyunu bilenler için | no interface at all. for those who know the game | |
| opt.mic | Microphone: {0} | Mikrofon: {0} | microphone: {0} | |
| opt.mic.default | default | varsayılan | default | |
| opt.mute | {0}: mute | {0}: sustur | {0}: silence | |
| opt.unmute | {0}: unmute | {0}: sesi aç | {0}: open the sound | |
| opt.nobody | nobody else here to mute | burada susturacak başka kimse yok | here there is nobody else to silence | |
| inspect.add | add friend on Steam | Steam'de arkadaş ekle | on Steam add friend | the apostrophe suffix is Turkish grammar for proper nouns |
| inspect.close | I closes | I kapatır | I closes | |
| menu.leave | Leave lobby | Lobiden ayrıl | from the lobby leave | |
| menu.delete | Delete lobby | Lobiyi sil | the lobby delete | |
| menu.play | PLAY | OYNA | PLAY | |
| lobby.readycall | ready check. B yes, C no | hazır mısınız? B evet, C hayır | are you all ready? B yes, C no | |
| lobby.ready.on | READY {0}/{1} | HAZIR {0}/{1} | READY {0}/{1} | |
| lobby.ready.off | READY {0}/{1}. B when ready | HAZIR {0}/{1}. hazır olunca B | READY {0}/{1}. when ready, B | |
| stand.title | the book stand | kitap sehpası | book stand | |
| stand.hostprivate | create private lobby (invite only) | özel lobi kur (sadece davetle) | private lobby set up (only by invitation) | |
| stand.hostpublic | CREATE PUBLIC LOBBY | AÇIK LOBİ KUR | OPEN LOBBY SET UP | |
| stand.pw | password (optional) | şifre (isteğe bağlı) | password (optional) | |
| stand.code | entry code (optional) | giriş kodu (isteğe bağlı) | entry code (optional) | |
| stand.hint | walk away to close | kapatmak için uzaklaş | to close, move away | |
| stand.map | change map | haritayı değiştir | the map, change | |
| stand.share | acolytes at least {0}% | en az %{0} mürit | at least %{0} disciples | mürit = disciple, follower; a native word kids know, "akolit" is not Turkish |
| stand.setcode | set code | kod belirle | code set | |
| stand.readycall | ready check | hazır mısınız? | are you all ready? | |
| stand.invite | invite friends | arkadaşlarını davet et | your friends, invite | |
| stand.start | START | BAŞLA | START | |
| stand.waiting | waiting for ready | hazır olmaları bekleniyor | their being ready is awaited | |
| stand.delete | delete lobby | lobiyi sil | the lobby delete | |
| stand.kick | kick | at | throw (out) | |
| stand.ban | ban | yasakla | forbid | |
| stand.banned | banned: {0} | yasaklı: {0} | forbidden: {0} | |
| stand.unban | unban | yasağı kaldır | lift the ban | |
| stand.name | lobby name | lobi adı | lobby name | |
| stand.size | size {0} | kişi {0} | persons {0} | |
| stand.region | region: {0} | bölge: {0} | region: {0} | |
| stand.tab.host | HOST | KUR | SET UP | |
| stand.tab.join | JOIN | KATIL | JOIN | |
| stand.settings | Settings | Ayarlar | settings | |
| stand.regions | Regions | Bölgeler | regions | |
| stand.langs | Languages | Diller | languages | |
| stand.behaviors | Behaviors | Hava | air (vibe) | hava is the Turkish idiom for the mood of a group |
| stand.duration | time {0} min | süre {0} dk | duration {0} min | dk = dakika |
| stand.setpw | set password | şifre belirle | password set | |
| stand.hosting | HOSTING your lobby | Lobiyi SEN yönetiyorsun | the lobby, YOU are managing | |
| stand.players | Players | Oyuncular | players | |
| filter.all | all | hepsi | all of them | |
| browse.refresh | refresh | yenile | renew | |
| browse.join | JOIN | KATIL | JOIN | |
| browse.locked | (password) | (şifreli) | (with password) | |
| browse.none | no lobbies found. host one! | lobi bulunamadı. sen kur! | lobby not found. you set one up! | |
| browse.needpw | this lobby wants a password | bu lobi şifre istiyor | this lobby wants a password | |
| browse.cancel | cancel | iptal | cancel | |
| region. | any region | her bölge | every region | |
| region.eu | Europe | Avrupa | Europe | |
| region.na | North America | Kuzey Amerika | North America | |
| region.sa | South America | Güney Amerika | South America | |
| region.asia | Asia | Asya | Asia | |
| region.oce | Oceania | Okyanusya | Oceania | |
| region.mea | Middle East & Africa | Orta Doğu ve Afrika | Middle East and Africa | |
| tag.welcome | everyone welcome | herkes hoş geldi | everyone has come well (welcome) | |
| tag.beginners | beginners welcome | yeni başlayanlar hoş geldi | new starters welcome | |
| tag.casual | casual fun | keyif için | for pleasure | |
| tag.tryhard | try hards | ciddi | serious | |
| tag.mic | mic on | mikrofon açık | microphone open (on) | |
| tag.quiet | quiet ok | sessiz de olur | quiet also works | |
| opt.mic.title | Microphone | Mikrofon | microphone |  |
| opt.mic.open | Open mic | Hep açık | always open |  |
| opt.mic.ptt | Hold V | V basılı | V pressed | V is a physical key, kept |
| opt.mic.off | Muted | Kapalı | closed |  |
| opt.tab.game | Game | Oyun | game |  |
| opt.tab.video | Video | Görüntü | image |  |
| opt.tab.audio | Audio | Ses | sound |  |
| opt.resolution | Resolution: {0} | Çözünürlük: {0} | resolution: {0} |  |
| opt.resolution.title | Resolution | Çözünürlük | resolution |  |
| opt.display | Display | Ekran | screen |  |
| opt.display.full | Fullscreen | Tam ekran | full screen |  |
| opt.display.borderless | Borderless | Kenarlıksız | without edge |  |
| opt.display.windowed | Windowed | Pencere | window |  |
| opt.quality | Quality | Kalite | quality |  |
| opt.low | Low | Düşük | low |  |
| opt.medium | Medium | Orta | middle |  |
| opt.high | High | Yüksek | high |  |
| opt.textures | Textures | Dokular | textures |  |
| opt.shadows | Shadows | Gölgeler | shadows |  |
| opt.effects | Effects | Efektler | effects |  |
| opt.motionblur | Motion blur | Hareket bulanıklığı | movement blur |  |
| opt.aa | Antialiasing | Kenar yumuşatma | edge softening | the Turkish term |
| opt.fps | Frame limit | Kare sınırı | frame limit |  |
| opt.vsync | VSync | Dikey eşitleme | vertical sync |  |
| opt.off | Off | Kapalı | closed |  |
| opt.on | On | Açık | open |  |
| opt.music | Music: {0}% | Müzik: %{0} | music: %{0} | percent sign before the number |
| opt.sfx | Sounds: {0}% | Sesler: %{0} | sounds: %{0} | percent sign before the number |
| seal.norune | no rune here to seal. aim at one of your runes | burada mühürlenecek rün yok. kendi rünlerinden birine nişan al | here there is no rune to be sealed. take aim at one of your own runes |  |
| seal.noink | not enough ink for the seal | mühür için mürekkep yetmiyor | for the seal the ink does not suffice |  |
| rune.noink | not enough ink to finish the rune | rünü bitirmek için mürekkep yetmiyor | to finish the rune the ink does not suffice |  |
| round.safe | THE LOBBY IS SAFE GROUND | LOBİ GÜVENLİ BÖLGE | lobby safe zone |  |
| round.versus | WIZARDS vs ACOLYTES | BÜYÜCÜLER MÜRİTLERE KARŞI | wizards against acolytes | mürit = acolyte, as in the rest of the file |
| round.wizards | WIZARDS WIN | BÜYÜCÜLER KAZANDI | wizards won |  |
| round.acolytes | ACOLYTES WIN | MÜRİTLER KAZANDI | acolytes won |  |
| round.home | {0}. back to the lobby in {1} | {0}. {1} sonra lobiye dönüş | {0}. {1} later return to lobby | {1} is seconds |
| round.pot |  · pot {0}% |  · kazan %{0} | cauldron %{0} | percent sign before the number, Turkish order |
| round.green |  · the pot is GREEN |  · kazan YEŞİL | cauldron GREEN |  |
| gate.accepts | THE GATE ACCEPTS | KAPI KABUL ETTİ | gate accepted |  |
| net.hostleft | THE HOST LEFT | HOST GİTTİ | host went | host is the word Turkish players use |
| net.hosting | ● HOSTING, {0} player(s) | ● HOST SENSİN, {0} oyuncu | host is you, {0} player | Turkish needs no plural after a number |
| net.connected | ● CONNECTED, {0} player(s) | ● BAĞLANDIN, {0} oyuncu | you connected, {0} player |  |
| net.map | MAP: {0} | HARİTA: {0} | map: {0} |  |
| net.maplikes | MAP: {0} · ♥{1} | HARİTA: {0} · ♥{1} | map: {0} · ♥{1} |  |
| steam.offline | Steam not running, offline & LAN only | Steam açık değil, sadece çevrimdışı ve LAN | Steam open not, only offline and LAN |  |
| steam.ready | Steam ready: {0} | Steam hazır: {0} | Steam ready: {0} | {0} is the Steam name |
| steam.leavefirst | leave your lobby first | önce kendi lobinden ayrıl | first from own lobby leave |  |
| steam.ping | your ping to that host is {0}ms, lobby allows {1} | o hosta pingin {0} ms, lobi izni {1} | to that host your ping {0} ms, lobby permission {1} |  |
| steam.joining | joining… | giriliyor… | being entered… |  |
| steam.deleted | lobby deleted | lobi silindi | lobby deleted |  |
| steam.notrunning | Steam not running | Steam açık değil | Steam open not |  |
| steam.creating | creating lobby… | lobi kuruluyor… | lobby being set up… |  |
| steam.failed | lobby failed: {0} | lobi kurulamadı: {0} | lobby could not be set up: {0} | {0} is a Steam error code |
| steam.noenter | couldn't enter the lobby | lobiye girilemedi | into lobby could not be entered |  |
| steam.nohost | lobby has no host, try again | lobinin hostu yok, bir daha dene | lobby's host there is none, once more try |  |
| steam.connecting | joined, connecting… | girdin, bağlanıyor… | you entered, connecting… |  |
| steam.private | PRIVATE LOBBY, invite friends | ÖZEL LOBİ, arkadaşlarını çağır | private lobby, call your friends |  |
| steam.public | PUBLIC LOBBY, listed | AÇIK LOBİ, listede | open lobby, in the list |  |
| menu.tagline | draw fast. die funny. | hızlı çiz. komik öl. | fast draw. funny die. |  |
| menu.close | Close | Kapat | close |  |
| chip.pages | turn the pages | sayfaları çevir | the pages turn | the mouse wheel turns the grimoire pages |
| round.potopens |  · pot opens in {0} |  · kazan {0} içinde açılır | cauldron opens in {0} | seconds; banner word while the pot brews / while the ink hops to the next pot |
| round.inkflight |  · ink in flight {0} |  · mürekkep uçuşta {0} | ink in flight {0} | seconds; banner word while the pot brews / while the ink hops to the next pot |
| stand.lanhost | LAN host | LAN kur | set up LAN | book stand, the LAN row: host over the local network |
| stand.lanjoin | LAN join | LAN'a katıl | join LAN | book stand, the LAN row: join the address in the field |
| opt.uiscale | UI size: {0}% | Arayüz boyutu: %{0} | interface size: {0}% | options, game tab: the slider that scales every panel and chip |
| stand.nocap | no cap | sınırsız | unlimited | book stand, the size row at Steam's ceiling of 250: the host set no cap |
| stand.heavy | your connection carries everyone | bağlantın herkesi taşır | your connection carries everyone | book stand, under the size row past 32 players: the host's upload carries the lobby |
