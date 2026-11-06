# Звуки сторонних авторов

Лежат в `Assets/_Project/Resources/Audio`. Обработаны: моно, обрезка тишины, выравнивание громкости, смешивание.

## Oculus Audio Pack 01 — CC BY 4.0 (нужно указать автора)
© 2015 Oculus VR, LLC. Лицензия: https://creativecommons.org/licenses/by/4.0/
Источник: https://github.com/facebookincubator/meta-open-sound (corpora/oculus-audio-pack)

- `swish_1..6` — SWSH_Swoosh_OS_01..06
- `hit_1..4`, `hitheavy_1..3`, `stab_1` — смесь Kenney knifeSlice с FGHTImpt_PunchRegular/PunchHarder
- `step_1..5` — FEETHmn_ShoeDirtWalk_OS_01..05; `stepsnow_1..5` — FEETHmn_ShoeSnowWalk_OS_01..05
- `rain_loop` — RAINVege_ForestRain_LOOP_01

Строка для титров/страницы магазина:
«Звуки: Oculus Audio Pack 01 © 2015 Oculus VR, LLC, CC BY 4.0; Kenney (kenney.nl), CC0».

## StarNinjas — CC0
«20 Sword Sound Effects (Attacks and Clashes)», https://opengameart.org/content/20-sword-sound-effects-attacks-and-clashes
- `clang_1..10` — sword_clash.1..10 (звон клинка о клинок: блок, парирование, сломленное равновесие)

## Kenney — CC0 (без условий)
Источник: https://github.com/ismetcabbar9-oss/kenney-audio-assets (RPG Audio, Casino Audio, kenney.nl)

- `knifeSlice`, `knifeSlice2` — в составе `hit_*`, `hitheavy_*`, `stab_1`
- `dicecup_1..3` — dieShuffle1..3; `dice_1..3` — diceThrow1..3; `coins_1..2` — handleCoins, handleCoins2

## Подложки карты — CC0, Freesound (через https://github.com/funcoder/omarchy-ambient)
Сделаны бесшовными петлями, громкость выровнена.

- `amb_birds` — «Forest quiet atmosphere with some birds», felix.blume (freesound.org/s/414098)
- `amb_wind` — «Wind on bushes with muffled gust of wind», felix.blume (freesound.org/s/711106)
- `amb_stream` — «Relaxing River Sound», IceVFX (freesound.org/s/722875)

## Подложка трактира — CC BY 4.0, Oculus Audio Pack 01
- `amb_tavern` — CRWDConv_Restaurant_LOOP_01

## Эффекты
- Линии скорости в `Shaders/PostFX.shader` — своя реализация по идее MirzaBeig/Anime-Speed-Lines (Unlicense), код не скопирован.
- Рассыпание пеплом в `Shaders/Character.shader` — своя реализация по идее AdultLink/VerticalDissolve (MIT).

## Музыка
- `music_battle` — «Shamisen Samurai Rock» (loop), MOMIZizm MUSiC, Free Japanese Music Pack — https://momiziba.itch.io/free-japanese-music-pack
  Условие автора: «Free Music : MOMIZizm MUSiC» (music.storyinvention.com).
