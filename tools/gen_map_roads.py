"""Дороги карт похода: tools/map_roads.json → C#.

Дороги проложены вручную по нарисованным тропам, мостам и лестницам трёх карт
(проверка: ни один маршрут между соседними слоями не пересекает воду или лаву, кроме мостов).
С 2026-11-07 карты расширены на восток: map_roads.json собирает tools/map_extend/extend_roads.py из map_roads_old1536.json.
Скрипт переписывает два блока между маркерами:
  Campaign.cs      — // ROADS-SPOTS BEGIN … // ROADS-SPOTS END   (места на карте по слоям)
  TerrainRoads.cs  — // ROADS-GRAPH BEGIN … // ROADS-GRAPH END   (узлы дорог и связи)
Запуск: python3 tools/gen_map_roads.py
"""
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / "tools/map_roads.json").read_text(encoding="utf-8"))
NAMES = ["лес теней", "горы они", "сёгунат"]


def f(v):
    return ("%.3f" % v).rstrip("0").rstrip(".") + "f"


def spots_block():
    out = ["        // ROADS-SPOTS BEGIN (сгенерировано tools/gen_map_roads.py из tools/map_roads.json — правьте JSON)",
           "        static readonly Vector2[][][] Spots =", "        {"]
    for r in "012":
        out.append("            new[]   // " + NAMES[int(r)])
        out.append("            {")
        for layer in DATA[r]["spots"]:
            out.append("                new[] { " + ", ".join("new Vector2(%s, %s)" % (f(x), f(y)) for _, x, y in layer) + " },")
        out.append("            },")
    out.append("        };")
    out.append("        // бывшие логова старых карт (до расширения на восток) — теперь сильный противник по дороге к владыке")
    out.append("        static readonly Vector2[] LairSpots = { " + ", ".join("new Vector2(%s, %s)" % (f(DATA[r]["lair"][0]), f(DATA[r]["lair"][1])) for r in "012") + " };")
    out.append("        // ROADS-SPOTS END")
    return "\n".join(out)


def graph_block():
    out = ["        // ROADS-GRAPH BEGIN (сгенерировано tools/gen_map_roads.py из tools/map_roads.json — правьте JSON)",
           "        static void MakeGraphs()", "        {", "            graphs = new Graph[3];"]
    for r in "012":
        m = DATA[r]
        pos = {}
        for layer in m["spots"]:
            for n, x, y in layer:
                pos[n] = (x, y)
        for k, v in m["points"].items():
            pos[k] = tuple(v)
        bridge = set(m["bridge"])
        names, links, cnt = list(pos), [], 0
        for a, mids, b in m["roads"]:
            prev = a
            for p in mids:
                cnt += 1
                k = "_%d" % cnt
                pos[k] = tuple(p); names.append(k)
                links.append((prev, k)); prev = k
            links.append((prev, b))
        idx = {n: i for i, n in enumerate(names)}
        out.append("            // " + NAMES[int(r)] + ": %d узлов дорог, %d отрезков; мост — %s" % (len(names), len(links), ", ".join(m["bridge"])))
        out.append("            graphs[%s] = Make(new float[] {" % r)
        row = []
        for n in names:
            x, y = pos[n]
            row.append("%s, %s, %d" % (f(x), f(y), 2 if n in bridge else 0))
        for i in range(0, len(row), 6):
            out.append("                " + ", ".join(row[i:i + 6]) + ",")
        out.append("            }, new int[] {")
        pairs = ["%d, %d" % (idx[a], idx[b]) for a, b in links]
        for i in range(0, len(pairs), 10):
            out.append("                " + ", ".join(pairs[i:i + 10]) + ",")
        out.append("            });")
    out.append("        }")
    out.append("        // ROADS-GRAPH END")
    return "\n".join(out)


def patch(rel, begin, end, block):
    p = ROOT / rel
    s = p.read_text(encoding="utf-8")
    pat = re.compile(r"[ \t]*// " + begin + r".*?// " + end, re.S)
    assert pat.search(s), "markers missing in " + rel
    p.write_text(pat.sub(lambda _: block, s, count=1), encoding="utf-8")


if __name__ == "__main__":
    patch("Assets/_Project/Scripts/Game/Campaign.cs", "ROADS-SPOTS BEGIN", "ROADS-SPOTS END", spots_block())
    patch("Assets/_Project/Scripts/Game/TerrainRoads.cs", "ROADS-GRAPH BEGIN", "ROADS-GRAPH END", graph_block())
    print("roads: Campaign.Spots and TerrainRoads graphs regenerated")
