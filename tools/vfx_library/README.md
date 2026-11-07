# Библиотека эффектов (CC0)

`sh tools/vfx_library/fetch.sh` — скачать исходники (≈110 МБ) в tools/vfx_library/src (не коммитится).
Превью всех листов: catalog_flipbooks.jpg, catalog_predrawn.jpg.

## Brackeys VFX Bundle — github.com/Brackeys/vfx-in-godot (CC0; Kenney, Picster, Thomas Iché, CodeManu)

Покадровые листы (имя_КОЛxСТР): все кадры по порядку слева направо, сверху вниз.

flipbooks/ (1024², дым/огонь/взрывы, зацикленные):
- cloud_01_8x8.tga
- cloud_02_8x8.tga
- explosion_01_8x8.tga
- explosion_02_8x8.tga
- explosion_smoke_01_8x8.tga
- fire_01_8x8.tga
- fire_02_8x8.tga
- fire_03_8x8.tga
- fire_04_8x8.tga
- flame_01_16x4.tga
- flame_02_15x4.tga
- wispy_smoke_01_8x8.tga
- wispy_smoke_02_8x8.tga
- wispy_smoke_03_8x8.tga

predrawn/ (нарисованные вспышки/удары):
- big_hit_6x5.png 3342x2765
- blood_impact_6x5.png 414x300
- charge_7x6.png 2247x2226
- dithered_fire_6x5.png 3102x1230
- electric_ring_6x5.png 1590x1325
- explosion_6x5.png 2130x1775
- fire_point_6x5.png 1038x965
- fire_ring_6x5.png 2526x2125
- impact_white_6x4.png 1746x1505
- lightstreaks_6x5.png 3102x2575
- star_explosion_6x5.png 840x654
- vortex_6x5.png 2562x2155
- wavy_blue_6x5.png 2622x2185
- wavy_purple_6x5.png 2718x1685

particles/alpha/ — одиночные спрайты частиц (Kenney): circle, dirt, fire, flame, flare, light, magic, muzzle, scorch,
scratch, slash, smoke, spark, spotlight, star, symbol, trace, twirl, window (по 1–10 вариантов).

## Kenney Particle Pack — github.com/Calinou/kenney-particle-pack (CC0)

addons/kenney_particle_pack/*.png — те же 80 спрайтов + 16 повёрнутых (trace/muzzle/spark/flame *_rotated).

## Уже в игре

- UI/fx_smoke_wisp.png ← flipbooks/wispy_smoke_01_8x8.tga: дым печей и труб на карте (MapLife), 64 кадра;
  яркость поднята (1−(1−x)^3.2), сохранён как серый+альфа
