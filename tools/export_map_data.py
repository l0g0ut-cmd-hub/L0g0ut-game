"""Карты похода → файлы карт (Assets/_Project/Resources/Maps/map_<n>.json), которые читает игра и правит редактор карт.

Источники: tools/map_roads.json (места по рядам, развилки, мосты, дороги), доп. места (Campaign.ExtraSpots, в координатах
старых картин 1536), ответвление у древа в Лесу теней (места с постоянным типом, тропы и переправа), водопады и ручей
Леса теней (MapLife.SheetFalls). Координаты мест и дорог — доли широкой карты (x/ширина, y/1024); эффекты — пиксели картины.
Запуск: python3 tools/export_map_data.py
"""
import json, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[1]
R = json.loads((ROOT / "tools/map_roads.json").read_text(encoding="utf-8"))
OUT = ROOT / "Assets/_Project/Resources/Maps"; OUT.mkdir(parents=True, exist_ok=True)
NAMES = ["ЛЕС ТЕНЕЙ", "ГОРЫ ОНИ", "ПРОКЛЯТЫЙ СЁГУНАТ"]
EXTRA = {"0": [(0.33, 0.636, 1), (0.212, 0.835, 1)], "1": [(0.275, 0.69, 1)], "2": [(0.24, 0.545, 1), (0.91, 0.33, 5), (0.8, 0.432, 4)]}
TYPES = {"TreeShadow": 17, "KamiRoots": 18, "FisherHut": 19, "HermitIsle": 20}   # NodeType (Campaign.cs)
r4 = lambda v: round(v, 4)

for r in "012":
    m = R[r]; W = m["width"]
    places, points, roads, effects = [], [], [], []
    for li, layer in enumerate(m["spots"]):
        for n, x, y in layer: places.append({"id": n, "x": r4(x), "y": r4(y), "layer": li, "type": -1, "kind": 0, "bridge": n in set(m["bridge"])})
    for k, (x, y, l) in enumerate(EXTRA[r]):
        places.append({"id": "X%d" % (k + 1), "x": r4(x * 1536 / W), "y": r4(y), "layer": l, "type": -1, "kind": 1})
    bridge = set(m["bridge"])
    for k, v in m["points"].items(): points.append({"id": k, "x": r4(v[0]), "y": r4(v[1]), "bridge": k in bridge})
    for a, mids, b in m["roads"]:
        roads.append({"a": a, "b": b, "pts": [r4(c) for p in mids for c in p], "water": False, "trail": False})
    if r == "0":
        # ответвление у древа (2026-11-18): места с постоянным типом, развилка GJ, тропа за древом, переправа на остров
        for gid, (x, y, t, l) in {"G1": (0.6513, 0.2441, "TreeShadow", 6), "G2": (0.6981, 0.4648, "KamiRoots", 6),
                                  "G3": (0.8966, 0.2773, "FisherHut", 7), "G4": (0.8218, 0.1982, "HermitIsle", 8)}.items():
            places.append({"id": gid, "x": x, "y": y, "layer": l, "type": TYPES[t], "kind": 2})
        points.append({"id": "GJ", "x": 0.6226, "y": 0.3086, "bridge": False})
        def road(a, b, mids, water=False): roads.append({"a": a, "b": b, "pts": [c for p in mids for c in p], "water": water, "trail": not water})
        road("B5", "GJ", [(0.5594, 0.3145), (0.5977, 0.3135)])
        road("GJ", "G1", [(0.6379, 0.2734)])
        road("GJ", "G3", [(0.6418, 0.2852), (0.6743, 0.2754), (0.7126, 0.2773), (0.7471, 0.2891), (0.7701, 0.3145), (0.7931, 0.3594),
                          (0.8199, 0.3828), (0.8487, 0.3711), (0.8774, 0.3271)])
        road("GJ", "G2", [(0.6314, 0.376), (0.6475, 0.4297), (0.6724, 0.4609)])
        road("G2", "N1", [(0.6743, 0.5), (0.6475, 0.5195)])
        road("G3", "G4", [(0.8774, 0.2422), (0.8525, 0.2305), (0.8322, 0.209)], True)
        # водопады (x — середина, y — верх, w×h в пикселях картины) и ручей у озера
        for x, y, h, w, kind, a, foot in [(281, 74, 100, 36, "fall_fade", 0.62, 0), (298, 158, 70, 46, "fall_mid", 0.55, 0),
                                          (318, 205, 90, 58, "fall_mid", 0.55, 1), (405, 370, 44, 22, "fall_fade", 0.6, 1),
                                          (556, 550, 58, 26, "fall_fade", 0.6, 1), (2280, 397, 218, 62, "fall_lip", 0.9, 2)]:
            effects.append({"kind": kind, "x": x - w / 2, "y": y, "w": w, "h": h, "alpha": a, "speed": 1.0, "foot": foot})
        effects.append({"kind": "stream", "x": 2226, "y": 370, "w": 76, "h": 42, "alpha": 1.0, "speed": 1.0, "foot": 0})
    data = {"version": 1, "name": NAMES[int(r)], "texture": "UI/map_wide_" + r, "width": W, "order": int(r), "boss": int(r) + 1,
            "lairX": r4(m["lair"][0]), "lairY": r4(m["lair"][1]), "places": places, "points": points, "roads": roads, "effects": effects}
    (OUT / ("map_%s.json" % r)).write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    print(r, len(places), "мест,", len(points), "развилок,", len(roads), "дорог,", len(effects), "эффектов")
