# Localization review

Twelve languages, Meccha Chameleon's set minus Arabic (dropped Sep 4 2026).
English is compiled into `Assets/_Game/Scripts/Loc.cs`. Every other language
is one JSON file in `Assets/StreamingAssets/Loc/` and one review sheet in
`LocReview/`. Same 127 keys everywhere, checked by script and by the menu
`Spelly Zombie/Localization/Check translations for missing keys`.

## How to verify a line

Each sheet has one row per string:

| key | English | translation | literal back-translation | notes |

The back-translation is word by word, so a dictionary is enough to check it.
Notes appear only where a choice was made: politeness level, a game noun,
a word that has no equivalent, an idiom swap. An empty note means the line
is plain and literal.

To change a line, edit the JSON value. The key stays. Keep `{0}` and `{1}`
exactly, they are filled by the game. Keep the key letters (B, C, E, R, I,
ALT) untranslated, they are physical keys.

## Register

Every language speaks to the player like a friend, not a manual.

| language | file | register |
|---|---|---|
| Japanese | ja | plain form (だ/である level), no です/ます except one status line; kana for kid words |
| Chinese Simplified | zh-CN | plain, 你 |
| Chinese Traditional | zh-TW | plain, 你, Taiwan wording (儲存, 預設, 重新整理) |
| Korean | ko | 해체 / plain declarative, no 요 |
| Spanish | es | tú, Spain wording (coger, ordenador-free) |
| Portuguese (Brazil) | pt-BR | você, Brazilian wording (salvar, tela) |
| French | fr | tu, space before : ? ! as French typography wants |
| German | de | du, nouns capitalized as German needs |
| Italian | it | tu |
| Russian | ru | ты |
| Turkish | tr | sen |

## Game nouns

| English | ja | zh-CN | zh-TW | ko | es | pt-BR | fr | de | it | ru | tr |
|---|---|---|---|---|---|---|---|---|---|---|---|
| grimoire | グリモワール | 魔导书 | 魔導書 | 마도서 | grimorio | grimório | grimoire | Grimoire | grimorio | гримуар | büyü kitabı |
| acolyte | アコライト | 侍祭 | 侍祭 | 사도 | acólito | acólito | acolyte | Akolyth | accolito | аколит | mürit |
| seal | 魔法陣 | 法阵 | 法陣 | 마법진 | sello | selo | sceau | Siegel | sigillo | печать | mühür |
| rune | ルーン | 符文 | 符文 | 룬 | runa | runa | rune | Rune | runa | руна | rün |
| ink | インク | 墨水 | 墨水 | 잉크 | tinta | tinta | encre | Tinte | inchiostro | чернила | mürekkep |
| wand | 杖 | 魔杖 | 魔杖 | 지팡이 | varita | varinha | baguette | Zauberstab | bacchetta | палочка | asa |
| lobby | ロビー | 房间 | 房間 | 로비 | sala | sala | salon | Lobby | stanza | лобби | lobi |
| zombie | ゾンビ | 僵尸 | 殭屍 | 좀비 | zombi | zumbi | zombie | Zombie | zombi | зомби | zombi |
| ghost | 幽霊 | 幽灵 | 幽靈 | 유령 | fantasma | fantasma | fantôme | Geist | fantasma | призрак | hayalet |
| wizard | (dropped) | (dropped) | (dropped) | (dropped) | mago | mago | (dropped) | (dropped) | mago | (dropped) | (dropped) |

"wizard" appears only in "pose your wizard". Languages marked dropped say
"take a pose" because naming the wizard there reads oddly.

## Status

| language | strings | font | reviewed by Marko |
|---|---|---|---|
| ja | 127 | Noto Sans JP (assign on UISkin) | |
| zh-CN | 127 | Noto Sans SC (assign on UISkin) | |
| zh-TW | 127 | Noto Sans TC (assign on UISkin) | |
| ko | 127 | Noto Sans KR (assign on UISkin) | |
| es | 127 | skin font | |
| pt-BR | 127 | skin font | |
| fr | 127 | skin font | |
| de | 127 | skin font | |
| it | 127 | skin font | |
| ru | 127 | skin font, needs Cyrillic glyphs (OtherFont slot if the hand-drawn font lacks them) | |
| tr | 127 | skin font, needs ı ğ ş İ glyphs | |
