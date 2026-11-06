# Эффекты CGHEVEN (CC0)

Скачаны с https://cgheven.com (видео 1K ProRes 4444 / H.264, лицензия CC0) workflow `fx-fetch` (ветка `fx-fetch`, результат — ветка `fx-out`).
Видео → 48 кадров 256×256 → лист 6×6 (36 кадров), 1024×1024, на чёрном — для аддитивного FxSprite.

В игре (`Resources/FX`): `fx_burst_gold` (energy-burst-11), `fx_burst_red` (09), `fx_burst_white` (12), `fx_shock_ring` (magical-shockwave-01),
`fx_shield` (magical-shield-block-01, обесцвечен — красится tint), `fx_aura_blue/red/violet` (looping-magic-aura-01/04/05).
Остальные листы здесь — запас: другие всплески, магический огонь, свеча, молния, дымовая волна.

## Карта и окна
- `Resources/UI/fx_campfire.png` — Campfire 03 Loop Front (1K ProRes 4444), вся петля — 208 кадров 25 к/с, 16×13 ячеек 64×84, альфа из яркости.
- `Resources/UI/fx_mist.png` — Atmospheric Fog 09 Side, кадры 19–48, 6×5 ячеек 256×128, края смягчены, играет вперёд-назад.
- Платные (PRO) у CGHEVEN: дым из труб (smoke plume), пар (steam jet), стелющийся/падающий туман, деревянный огонь — их не брали.
- `Resources/UI/fx_puff.png` — 4 клуба из Atmospheric Fog 01 Front (2×2 по 128), края в овал: из них собраны дым трактира/привала/охотников и пар онсэна.

## Интерфейс (`Resources/UI/fx_ui_*`, рисует `UiFx`, сложение света)
Все кадры исходных видео при 25 к/с (не выборка), прозрачность из яркости:
- `fx_ui_flash_gold/white/red` — energy-burst-11/12/09, кадры 2–37 (36, 6×6 по 128): вспышка выбора сложности, надевания, улучшения.
- `fx_ui_ring` — magical-shockwave-01, кадры 40–79 (40, 8×5 по 192×64), обесцвечено: кольцо у ног путника / вокруг медальона / под вещью.
- `fx_ui_aura_blue/red/violet` — looping-magic-aura-01/04/05, вся петля (133/104/107 кадров, 12 в ряд по 128): пламя духа выбранного путника.
- Искры (cannon-spark) у CGHEVEN на деле — дымные клубы, не взяты.
