# Мокап ходьбы

Клипы ходьбы бойцов (вперёд, назад, приставной шаг вбок) переносятся на наш скелет из
CMU Graphics Lab Motion Capture Database — http://mocap.cs.cmu.edu (BVH-конверсия Bruce Hahne / cgspeed,
https://github.com/Shriinivas/cmubvh). База бесплатна для любого использования, включая коммерческое;
авторы просят указывать источник: «The data used in this project was obtained from mocap.cs.cmu.edu.
The database was created with funding from NSF EIA-0196217.»

| файл | клип CMU | что | цикл (кадры, 60 к/с) |
|---|---|---|---|
| cmu_143_32.bvh | 143_32 Walk | шаг вперёд | 612–679 |
| cmu_141_31.bvh | 141_31 Walk Backwards | шаг назад | 414–472 |
| cmu_141_33.bvh | 141_33 Walk Sideways, Foot to Foot | приставной шаг вбок (оба направления) | 718–786, 296–369 |

Перенос и запекание клипов — `Assets/_Project/Scripts/Editor/MocapBaker.cs` (при сборке сцены, под каждую модель отдельно).
Метка частоты кадров в этих BVH (120 к/с) неверна для субъектов 141/143 — реальная съёмка 60 к/с (по длине шага и темпу).
