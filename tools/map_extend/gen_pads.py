"""Площадки под домики: tools/map_pads.json (пиксели широкой карты, выбраны вручную по кандидатам cand_pads.py
и проверены check_pads.py: только открытая земля — не вода, не лава, не пропасть, не мост, не лес, не нарисованные дома)
→ Assets/_Project/Scripts/Game/MapPads.cs. Места без площадки — только значок (и в генерации там не бывает услуг с домиком)."""
import json, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[2]
P = json.loads((ROOT / "tools/map_pads.json").read_text(encoding="utf-8"))
D = json.loads((ROOT / "tools/map_roads.json").read_text(encoding="utf-8"))
EX = {'0': [('X1', 0.33, 0.636), ('X2', 0.212, 0.835)], '1': [('X1', 0.275, 0.69)], '2': [('X1', 0.24, 0.545), ('X2', 0.91, 0.33), ('X3', 0.8, 0.432)]}
f = lambda v: ("%.4f" % v).rstrip("0").rstrip(".") + "f"
rows = []
for r in "012":
    W = D[r]["width"]
    sp = {n: (x, y) for l in D[r]["spots"] for n, x, y in l}
    for n, x, y in EX[r]: sp[n] = (x * 1536 / W, y)
    items = []
    for n, (px, py, s) in P[r].items():
        x, y = sp[n]
        items.append("new Vector4(%s, %s, %s, %s)" % (f(x), f(y), f(px / W), f(py / 1024)))
    rows.append("            new[] { " + ", ".join(items) + " },")
src = """using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Площадки под домики и стоянки у мест на карте (сгенерировано tools/map_extend/gen_pads.py из tools/map_pads.json).
    /// x, y — место (доли широкой карты), z, w — основание домика. Площадки выбраны глазами по каждой карте: только открытая
    /// земля (не вода, лава, пропасть, мост, лес, не поверх нарисованных домов и друг друга). Нет площадки — домика нет.
    /// </summary>
    public static class MapPads
    {
        public const float Size = 53f;   // домик в точках карты (было 74 — налезал на соседей и на нарисованное)
        static readonly Vector4[][] Pads =
        {
%s
        };
        /// <summary>Основание домика у места (доли карты) или false, если там домик не ставят.</summary>
        public static bool Get(int region, float x, float y, out Vector2 at)
        {
            Vector4[] list = Pads[Mathf.Clamp(region, 0, Pads.Length - 1)];
            for (int i = 0; i < list.Length; i++)
                if (Mathf.Abs(list[i].x - x) < 0.003f && Mathf.Abs(list[i].y - y) < 0.004f) { at = new Vector2(list[i].z, list[i].w); return true; }
            at = Vector2.zero; return false;
        }
        public static bool Has(int region, float x, float y) { Vector2 a; return Get(region, x, y, out a); }
    }
}
""" % "\n".join(rows)
(ROOT / "Assets/_Project/Scripts/Game/MapPads.cs").write_text(src, encoding="utf-8")
print("MapPads.cs:", {r: len(P[r]) for r in P})
