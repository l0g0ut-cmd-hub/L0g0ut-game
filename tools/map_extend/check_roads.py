"""Наложение дорог (tools/map_roads.json) на широкие карты для проверки: check_N.jpg (вся карта) и зум по аргументам.
python3 tools/map_extend/check_roads.py N [x0 x1 y0 y1 scale out.jpg]"""
import json, sys, pathlib, cv2
ROOT = pathlib.Path(__file__).resolve().parents[2]
D = json.loads((ROOT / "tools/map_roads.json").read_text(encoding="utf-8"))
r = sys.argv[1]; m = D[r]
im = cv2.imread(str(ROOT / ("Assets/_Project/Resources/UI/map_wide_%s.jpg" % r)))
H, W = im.shape[:2]
pos = {}
for layer in m["spots"]:
    for n, x, y in layer: pos[n] = (x * W, y * H)
for k, v in m["points"].items(): pos[k] = (v[0] * W, v[1] * H)
segs = []
for a, mids, b in m["roads"]:
    pts = [pos[a]] + [(p[0] * W, p[1] * H) for p in mids] + [pos[b]]
    segs.append(pts)
def draw(img, x0, y0, sc):
    for pts in segs:
        for i in range(1, len(pts)):
            p, q = pts[i - 1], pts[i]
            cv2.line(img, (int((p[0] - x0) * sc), int((p[1] - y0) * sc)), (int((q[0] - x0) * sc), int((q[1] - y0) * sc)), (0, 0, 255), max(1, int(2 * sc)), cv2.LINE_AA)
        for p in pts[1:-1]: cv2.circle(img, (int((p[0] - x0) * sc), int((p[1] - y0) * sc)), max(2, int(2 * sc)), (0, 200, 255), -1)
    for n, p in pos.items():
        X, Y = int((p[0] - x0) * sc), int((p[1] - y0) * sc)
        spot = any(n == s[0] for l in m["spots"] for s in l)
        col = (255, 0, 255) if n in m["bridge"] else (0, 255, 0) if spot else (255, 255, 0)
        cv2.circle(img, (X, Y), max(4, int(5 * sc)) if spot else max(3, int(3 * sc)), col, -1)
        if spot or n in m["bridge"]: cv2.putText(img, n, (X + 6, Y - 6), 0, 0.5, col, 2 if spot else 1)
if len(sys.argv) > 2:
    x0, x1, y0, y1 = [int(v) for v in sys.argv[2:6]]; sc = float(sys.argv[6])
    z = cv2.resize(im[y0:y1, x0:x1], None, fx=sc, fy=sc, interpolation=cv2.INTER_CUBIC)
    draw(z, x0, y0, sc); cv2.imwrite(sys.argv[7], z)
else:
    sc = 1500.0 / W; z = cv2.resize(im, None, fx=sc, fy=sc, interpolation=cv2.INTER_AREA)
    draw(z, 0, 0, sc); cv2.imwrite(str(ROOT / ("tools/map_extend/check_%s.jpg" % r)), z, [cv2.IMWRITE_JPEG_QUALITY, 85])
